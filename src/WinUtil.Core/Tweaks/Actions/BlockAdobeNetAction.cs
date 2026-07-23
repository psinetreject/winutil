using System.Text.RegularExpressions;
using WinUtil.Core.Abstractions;

namespace WinUtil.Core.Tweaks.Actions;

/// <summary>
/// Blocks Adobe's telemetry/activation endpoints by downloading the Ruddernation Adobe URL block list
/// and appending it to the system hosts file, then flushing DNS. Complex (DL). Undo strips the appended
/// block back out.
/// </summary>
public sealed partial class BlockAdobeNetAction : ICustomTweakAction
{
    public string Id => "WPFTweaksBlockAdobeNet";
    public bool SupportsUndo => true;

    private const string HostsUrl =
        "https://github.com/Ruddernation-Designs/Adobe-URL-Block-List/raw/refs/heads/master/hosts";

    private static string HostsPath =>
        Path.Combine(ActionHelpers.SystemRoot, "System32", "drivers", "etc", "hosts");

    // Matches the appended block ("#New Ver..." to end of file), single-line mode. Mirrors the PS
    // `-replace '(?s)#New Ver.*', ''`.
    [GeneratedRegex("#New Ver.*", RegexOptions.Singleline)]
    private static partial Regex AppendedBlockRegex();

    public async Task<OperationResult> ApplyAsync(TweakActionContext context)
    {
        var ct = context.CancellationToken;

        string blockList;
        try
        {
            using var http = new HttpClient();
            blockList = await http.GetStringAsync(new Uri(HostsUrl), ct);
        }
        catch (HttpRequestException ex)
        {
            return OperationResult.Fail($"Failed to download Adobe block list: {ex.Message}");
        }

        try
        {
            await File.AppendAllTextAsync(HostsPath, Environment.NewLine + blockList, ct);
        }
        catch (IOException ex)
        {
            return OperationResult.Fail($"Failed to update hosts file: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return OperationResult.Fail($"Failed to update hosts file (needs elevation): {ex.Message}");
        }

        await context.Process.RunAsync("ipconfig.exe", ["/flushdns"], ct);
        return OperationResult.Ok("Added Adobe URL block list to hosts file");
    }

    public async Task<OperationResult> UndoAsync(TweakActionContext context)
    {
        var ct = context.CancellationToken;

        try
        {
            var current = await File.ReadAllTextAsync(HostsPath, ct);
            var stripped = AppendedBlockRegex().Replace(current, string.Empty);
            await File.WriteAllTextAsync(HostsPath, stripped, ct);
        }
        catch (IOException ex)
        {
            return OperationResult.Fail($"Failed to update hosts file: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return OperationResult.Fail($"Failed to update hosts file (needs elevation): {ex.Message}");
        }

        await context.Process.RunAsync("ipconfig.exe", ["/flushdns"], ct);
        return OperationResult.Ok("Removed Adobe URL block list from hosts file");
    }
}
