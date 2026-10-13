using System.Collections.Generic;

namespace MiniThemeStudio.Core.Firmware;

public enum BaseInstallStatus
{
    Valid,
    Incomplete,
    Missing
}

public sealed record InstalledBaseInfo(
    FirmwareDefinition Definition,
    string LocalPath,
    BaseInstallStatus Status,
    IReadOnlyList<string> MissingFiles);
