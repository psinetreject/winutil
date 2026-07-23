using CommunityToolkit.Mvvm.ComponentModel;
using WinUtil.Core.Abstractions;
using WinUtil.Core.Models;
using WinUtil.Core.Tweaks;

namespace WinUtil.App.ViewModels;

/// <summary>
/// A toggle tweak whose on/off state reflects the live system (via <see cref="ITweakEngine.GetToggleState"/>)
/// and applies/undoes immediately when flipped. Reverts the visual state if the engine reports failure.
/// </summary>
public partial class ToggleTweakViewModel : ObservableObject
{
    private readonly ITweakEngine _engine;
    private readonly ShellState _shell;
    private bool _suppress;

    public ToggleTweakViewModel(Tweak tweak, ITweakEngine engine, ShellState shell)
    {
        Tweak = tweak;
        _engine = engine;
        _shell = shell;

        // Seed the field directly so the initial read does not trigger an apply/undo.
        try
        {
            _isOn = engine.GetToggleState(tweak);
        }
        catch
        {
            _isOn = tweak.DefaultState ?? false;
        }
    }

    public Tweak Tweak { get; }

    public string Content => Tweak.Content ?? Tweak.Id;
    public string? Description => Tweak.Description;

    [ObservableProperty]
    private bool _isOn;

    public bool Matches(string term) =>
        string.IsNullOrWhiteSpace(term)
        || Content.Contains(term, StringComparison.OrdinalIgnoreCase)
        || (Description?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false);

    partial void OnIsOnChanged(bool value)
    {
        if (_suppress)
        {
            return;
        }

        _ = ApplyToggleAsync(value);
    }

    private async Task ApplyToggleAsync(bool value)
    {
        if (_shell.IsBusy)
        {
            Revert(value);
            _shell.Report(TaskProgress.Failed("Another operation is running — try again shortly."));
            return;
        }

        _shell.IsBusy = true;
        _shell.Report(TaskProgress.Indeterminate($"{(value ? "Enabling" : "Disabling")} {Content}…"));
        try
        {
            // Run the engine off the UI thread so a blocking custom action (e.g. restarting Explorer)
            // can never freeze the window.
            var result = await Task.Run(() => value
                ? _engine.ApplyAsync(Tweak, _shell.Progress)
                : _engine.UndoAsync(Tweak, _shell.Progress)).ConfigureAwait(true);

            if (result.Success)
            {
                _shell.Report(TaskProgress.Completed(result.Message ?? $"{Content} updated."));
            }
            else
            {
                Revert(value);
                _shell.Report(TaskProgress.Failed(result.Message ?? $"Failed to update {Content}."));
            }
        }
        catch (Exception ex)
        {
            Revert(value);
            _shell.Report(TaskProgress.Failed(ex.Message));
        }
        finally
        {
            _shell.IsBusy = false;
        }
    }

    private void Revert(bool attempted)
    {
        _suppress = true;
        IsOn = !attempted;
        _suppress = false;
    }
}
