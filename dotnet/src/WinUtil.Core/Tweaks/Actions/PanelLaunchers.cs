using WinUtil.Core.Abstractions;

namespace WinUtil.Core.Tweaks.Actions;

/// <summary>
/// Shared base for the 14 legacy control-panel / MMC launcher tweaks (Trivial, NOUNDO). Each subclass
/// supplies only its shell target. Launch is fire-and-forget via <c>cmd /c start</c>, which uses the
/// shell association (so .cpl / .msc / shell verbs / exe all work) and returns immediately instead of
/// blocking on the launched window.
/// </summary>
public abstract class PanelLauncherActionBase : ICustomTweakAction
{
    public abstract string Id { get; }

    /// <summary>The applet, console, shell verb, or exe to launch (e.g. "main.cpl", "compmgmt.msc").</summary>
    protected abstract string ShellTarget { get; }

    public bool SupportsUndo => false;

    public async Task<OperationResult> ApplyAsync(TweakActionContext context)
    {
        // The empty-string element becomes `""` on the command line — `start`'s (empty) window title —
        // so the following token is treated as the command to launch.
        var result = await context.Process.RunAsync(
            "cmd.exe", ["/c", "start", "", ShellTarget], context.CancellationToken);

        return result.Succeeded
            ? OperationResult.Ok($"Launched {ShellTarget}")
            : OperationResult.Fail($"Failed to launch {ShellTarget}: {result.StandardError}");
    }

    public Task<OperationResult> UndoAsync(TweakActionContext context)
        => Task.FromResult(OperationResult.Ok());
}

/// <summary>Launches Computer Management (compmgmt.msc).</summary>
public sealed class ComputerAction : PanelLauncherActionBase
{
    public override string Id => "WPFPanelComputer";
    protected override string ShellTarget => "compmgmt.msc";
}

/// <summary>Launches the Control Panel.</summary>
public sealed class ControlAction : PanelLauncherActionBase
{
    public override string Id => "WPFPanelControl";
    protected override string ShellTarget => "control";
}

/// <summary>Launches Mouse Properties (main.cpl).</summary>
public sealed class MouseAction : PanelLauncherActionBase
{
    public override string Id => "WPFPanelMouse";
    protected override string ShellTarget => "main.cpl";
}

/// <summary>Launches Network Connections (ncpa.cpl).</summary>
public sealed class NetworkAction : PanelLauncherActionBase
{
    public override string Id => "WPFPanelNetwork";
    protected override string ShellTarget => "ncpa.cpl";
}

/// <summary>Launches Power Options (powercfg.cpl).</summary>
public sealed class PowerAction : PanelLauncherActionBase
{
    public override string Id => "WPFPanelPower";
    protected override string ShellTarget => "powercfg.cpl";
}

/// <summary>Launches Devices and Printers (shell folder).</summary>
public sealed class PrinterAction : PanelLauncherActionBase
{
    public override string Id => "WPFPanelPrinter";
    protected override string ShellTarget => "shell:::{A8A91A66-3A7D-4424-8D24-04E180695C7A}";
}

/// <summary>Launches Programs and Features (appwiz.cpl).</summary>
public sealed class ProgramsAction : PanelLauncherActionBase
{
    public override string Id => "WPFPanelPrograms";
    protected override string ShellTarget => "appwiz.cpl";
}

/// <summary>Launches Region settings (intl.cpl).</summary>
public sealed class RegionAction : PanelLauncherActionBase
{
    public override string Id => "WPFPanelRegion";
    protected override string ShellTarget => "intl.cpl";
}

/// <summary>Launches Security and Maintenance (wscui.cpl).</summary>
public sealed class SecurityAction : PanelLauncherActionBase
{
    public override string Id => "WPFPanelSecurity";
    protected override string ShellTarget => "wscui.cpl";
}

/// <summary>Launches Sound settings (mmsys.cpl).</summary>
public sealed class SoundAction : PanelLauncherActionBase
{
    public override string Id => "WPFPanelSound";
    protected override string ShellTarget => "mmsys.cpl";
}

/// <summary>Launches System Properties (sysdm.cpl).</summary>
public sealed class SystemAction : PanelLauncherActionBase
{
    public override string Id => "WPFPanelSystem";
    protected override string ShellTarget => "sysdm.cpl";
}

/// <summary>Launches Date and Time (timedate.cpl).</summary>
public sealed class TimedateAction : PanelLauncherActionBase
{
    public override string Id => "WPFPanelTimedate";
    protected override string ShellTarget => "timedate.cpl";
}

/// <summary>Launches Windows Defender Firewall (firewall.cpl).</summary>
public sealed class FirewallAction : PanelLauncherActionBase
{
    public override string Id => "WPFPanelFirewall";
    protected override string ShellTarget => "firewall.cpl";
}

/// <summary>Launches the System Restore UI (rstrui.exe).</summary>
public sealed class RestoreAction : PanelLauncherActionBase
{
    public override string Id => "WPFPanelRestore";
    protected override string ShellTarget => "rstrui.exe";
}
