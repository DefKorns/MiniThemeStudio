namespace MiniThemeStudio.Core.Firmware;

public enum BaseInstallStatus
{
    Valid,
    Invalid,
    Missing,
}

public sealed record InstalledBaseInfo(
    FirmwareDefinition Definition,
    string LocalPath,
    BaseInstallStatus Status,
    string? Problem = null);
