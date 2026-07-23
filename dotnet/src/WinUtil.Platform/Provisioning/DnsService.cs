using System.Diagnostics;
using System.Net.NetworkInformation;
using Microsoft.Extensions.Logging;
using WinUtil.Core.Abstractions;

namespace WinUtil.Platform.Provisioning;

/// <summary>
/// Sets or resets IPv4 and IPv6 DNS servers on every operational network adapter.
/// </summary>
/// <remarks>
/// Implementation note: this drives <c>netsh</c> rather than the WMI class
/// <c>root\StandardCimv2:MSFT_DNSClientServerAddress</c>. That CIM class exposes only a static
/// <c>Set</c> method taking a string[] of addresses keyed by (InterfaceIndex, AddressFamily),
/// which is clumsy to invoke through System.Management (array-typed method parameters, per-family
/// instances) and forces separate code paths for v4 and v6. <c>netsh interface ipv4|ipv6</c>
/// handles both families uniformly, and <c>validate=no</c> avoids the slow connectivity probe that
/// can otherwise make the call fail on offline networks. The IPv6 family (AF_INET6 / 23) is scoped
/// simply by issuing the commands under the <c>interface ipv6</c> context.
/// </remarks>
public sealed class DnsService(ILogger<DnsService> logger) : IDnsService
{
    private readonly ILogger<DnsService> _logger = logger;

    public OperationResult SetDns(string? primary, string? secondary, string? primary6, string? secondary6)
    {
        bool hasV4 = !string.IsNullOrWhiteSpace(primary);
        bool hasV6 = !string.IsNullOrWhiteSpace(primary6);
        if (!hasV4 && !hasV6)
        {
            return OperationResult.Fail("At least one primary DNS server (IPv4 or IPv6) must be provided.");
        }

        try
        {
            List<string> adapters = GetUpAdapterNames();
            if (adapters.Count == 0)
            {
                return OperationResult.Fail("No operational network adapters were found.");
            }

            int succeeded = 0;
            List<string> failures = [];

            foreach (string name in adapters)
            {
                bool ok = true;

                if (hasV4)
                {
                    ok &= Run(["interface", "ipv4", "set", "dnsservers", name, "static", primary!, "primary", "validate=no"]);
                    if (!string.IsNullOrWhiteSpace(secondary))
                    {
                        ok &= Run(["interface", "ipv4", "add", "dnsservers", name, secondary!, "index=2", "validate=no"]);
                    }
                }

                if (hasV6)
                {
                    ok &= Run(["interface", "ipv6", "set", "dnsservers", name, "static", primary6!, "primary", "validate=no"]);
                    if (!string.IsNullOrWhiteSpace(secondary6))
                    {
                        ok &= Run(["interface", "ipv6", "add", "dnsservers", name, secondary6!, "index=2", "validate=no"]);
                    }
                }

                if (ok)
                {
                    succeeded++;
                }
                else
                {
                    failures.Add(name);
                }
            }

            return Summarize(succeeded, failures, "set DNS servers");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set DNS servers.");
            return OperationResult.Fail($"Failed to set DNS servers: {ex.Message}");
        }
    }

    public OperationResult ResetToDhcp()
    {
        try
        {
            List<string> adapters = GetUpAdapterNames();
            if (adapters.Count == 0)
            {
                return OperationResult.Fail("No operational network adapters were found.");
            }

            int succeeded = 0;
            List<string> failures = [];

            foreach (string name in adapters)
            {
                bool ok = true;
                ok &= Run(["interface", "ipv4", "set", "dnsservers", name, "dhcp"]);
                ok &= Run(["interface", "ipv6", "set", "dnsservers", name, "dhcp"]);

                if (ok)
                {
                    succeeded++;
                }
                else
                {
                    failures.Add(name);
                }
            }

            return Summarize(succeeded, failures, "reset DNS to DHCP");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reset DNS to DHCP.");
            return OperationResult.Fail($"Failed to reset DNS to DHCP: {ex.Message}");
        }
    }

    private OperationResult Summarize(int succeeded, List<string> failures, string action)
    {
        if (succeeded == 0)
        {
            _logger.LogError("Could not {Action} on any adapter.", action);
            return OperationResult.Fail($"Could not {action} on any adapter.");
        }

        if (failures.Count > 0)
        {
            string joined = string.Join(", ", failures);
            _logger.LogWarning(
                "Applied '{Action}' on {Succeeded} adapter(s); failed on: {Failed}.",
                action, succeeded, joined);
            return OperationResult.Ok(
                $"Applied on {succeeded} adapter(s); could not apply on: {joined}.");
        }

        _logger.LogInformation("Applied '{Action}' on {Count} adapter(s).", action, succeeded);
        return OperationResult.Ok($"Applied on {succeeded} adapter(s).");
    }

    private static List<string> GetUpAdapterNames()
    {
        List<string> names = [];
        foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus == OperationalStatus.Up
                && ni.NetworkInterfaceType != NetworkInterfaceType.Loopback
                && ni.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
            {
                names.Add(ni.Name);
            }
        }

        return names;
    }

    private bool Run(string[] arguments)
    {
        // ArgumentList is used so adapter names containing spaces are quoted correctly.
        using Process process = new();
        process.StartInfo = new ProcessStartInfo("netsh.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string arg in arguments)
        {
            process.StartInfo.ArgumentList.Add(arg);
        }

        process.Start();
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            _logger.LogWarning(
                "netsh {Args} exited with {ExitCode}: {Output}",
                string.Join(' ', arguments), process.ExitCode, string.Concat(stdout, stderr).Trim());
            return false;
        }

        return true;
    }
}
