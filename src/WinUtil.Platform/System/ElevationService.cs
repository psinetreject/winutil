using System.Security.Principal;
using WinUtil.Core.Abstractions;

namespace WinUtil.Platform.System;

/// <summary>
/// Reports whether the current process is running with an elevated (Administrators) token,
/// via <see cref="WindowsPrincipal.IsInRole(WindowsBuiltInRole)"/>.
/// </summary>
public sealed class ElevationService : IElevationService
{
    public bool IsElevated
    {
        get
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
}
