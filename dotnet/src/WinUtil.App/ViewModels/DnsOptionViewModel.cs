using WinUtil.Core.Models;

namespace WinUtil.App.ViewModels;

/// <summary>A DNS choice on the Config tab. A null <see cref="Provider"/> means "reset to DHCP".</summary>
public sealed record DnsOptionViewModel(string Name, DnsProvider? Provider);
