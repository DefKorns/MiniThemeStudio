using System.IO;

namespace MiniThemeStudio.Core.Firmware;

public sealed record FirmwareDefinition(
    string Id,
    string Console,
    string Region,
    string Version,
    string DisplayName,
    string ArchivePattern,
    string UiInternalPath,
    string ConsoleMountPath,
    string OriginalMountPath,
    int? EngineFormat = null,
    IReadOnlyList<string>? SignatureFiles = null)
{
    public string RelativeStoragePath =>
        Path.Combine(Console.ToLowerInvariant(), Region.ToLowerInvariant(), Version.ToLowerInvariant());

    public string LegacyFolderName =>
        Console.ToLowerInvariant() switch
        {
            "snes" when Region.Equals("eur", StringComparison.OrdinalIgnoreCase) => "snes-eur",
            "snes" when Region.Equals("usa", StringComparison.OrdinalIgnoreCase) => "snes-usa",
            "snes" when Region.Equals("jpn", StringComparison.OrdinalIgnoreCase) => "snes-jpn",
            "nes" => "nes",
            "famicom" => "famicom",
            "shonen" => "shonen",
            _ => $"{Console}-{Region}".ToLowerInvariant()
        };
}
