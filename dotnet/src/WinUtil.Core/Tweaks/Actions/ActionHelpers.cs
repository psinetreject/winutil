using WinUtil.Core.Abstractions;

namespace WinUtil.Core.Tweaks.Actions;

/// <summary>Small shared primitives used by more than one custom tweak action.</summary>
internal static class ActionHelpers
{
    /// <summary>
    /// Converts a PowerShell-drive registry path ("HKCU:\Software\...") to the reg.exe form
    /// ("HKCU\Software\...") used when shelling out to <c>reg.exe delete</c> for recursive key removal
    /// (which the <see cref="IRegistryService"/> value API does not cover).
    /// </summary>
    public static string ToRegExePath(string psPath)
    {
        var colon = psPath.IndexOf(':');
        if (colon > 0 && colon + 1 < psPath.Length && psPath[colon + 1] == '\\')
        {
            return string.Concat(psPath.AsSpan(0, colon), psPath.AsSpan(colon + 1));
        }

        return psPath;
    }

    /// <summary>Best-effort <c>taskkill /F /IM</c> of a running image. Failures (process absent) are ignored.</summary>
    public static Task<ProcessResult> KillProcessAsync(IProcessRunner process, string imageName, CancellationToken cancellationToken)
        => process.RunAsync("taskkill.exe", ["/F", "/IM", imageName], cancellationToken);

    /// <summary>Deletes a directory and everything under it, tolerating locked/read-only entries.</summary>
    public static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
            // Locked file/handle still open — best effort, matching the PowerShell tool.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Deletes the <em>contents</em> of a directory (files and sub-directories) but keeps the directory
    /// itself. Mirrors <c>Remove-Item "$dir\*"</c>. Each top-level entry is removed independently so a
    /// single locked file does not abort the whole sweep.
    /// </summary>
    public static void TryDeleteContents(string root)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        foreach (var entry in Directory.EnumerateFileSystemEntries(root))
        {
            try
            {
                if (Directory.Exists(entry))
                {
                    Directory.Delete(entry, recursive: true);
                }
                else
                {
                    File.Delete(entry);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>%SystemRoot% (e.g. C:\Windows), falling back to the Windows special folder.</summary>
    public static string SystemRoot =>
        Environment.GetEnvironmentVariable("SystemRoot") ?? Environment.GetFolderPath(Environment.SpecialFolder.Windows);
}
