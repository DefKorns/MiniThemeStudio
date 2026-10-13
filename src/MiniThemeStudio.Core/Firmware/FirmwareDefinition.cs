using System.IO;

namespace MiniThemeStudio.Core.Firmware;

public sealed record FirmwareSource(
    string FileName,
    string Format,
    long Size,
    string Sha256,
    string FirmwareVersion);

public sealed record FirmwareDefinition(
    string Id,
    string Console,
    string Region,
    string Version,
    string DisplayName,
    string UiPath,
    int EngineFormat,
    string UiTreeSha256,
    int UiFileCount,
    IReadOnlyList<string> FirmwareVersions,
    IReadOnlyList<FirmwareSource> Sources)
{
    public string RelativeStoragePath => Path.Combine(Console, Region, Version);

    public string ConsoleUiPath => $"/{UiPath}";

    public string OriginalConsoleUiPath => $"/var/squashfs/{UiPath}";

    public string FirmwareVersionsText => string.Join(", ", FirmwareVersions);
}
