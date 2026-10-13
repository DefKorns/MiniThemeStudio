namespace MiniThemeStudio.Core.Firmware;

public interface IFirmwareRegistry
{
    IReadOnlyList<FirmwareDefinition> All { get; }

    FirmwareDefinition? FindById(string id);

    (FirmwareDefinition Definition, FirmwareSource Source)? FindBySourceSha256(string sha256);

    FirmwareDefinition? FindByUiTreeSha256(string sha256);
}
