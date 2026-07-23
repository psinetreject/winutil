namespace WinUtil.Core.Abstractions;

public enum ServiceStartupType
{
    Boot,
    System,
    Automatic,
    AutomaticDelayed,
    Manual,
    Disabled,
}

/// <summary>Changes Windows service startup types (idempotent). Missing services are non-fatal.</summary>
public interface IServiceManager
{
    OperationResult SetStartupType(string serviceName, ServiceStartupType startupType);
    ServiceStartupType? GetStartupType(string serviceName);
}
