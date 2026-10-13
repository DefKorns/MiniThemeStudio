using System.Collections.Generic;

namespace MiniThemeStudio.Core.Firmware;

public interface IFirmwareRegistry
{
    IReadOnlyList<FirmwareDefinition> All { get; }
    FirmwareDefinition? FindById(string id);
    FirmwareDefinition? MatchArchive(string fileName);
    FirmwareDefinition? MatchExtractedFolder(string directoryPath);
    void RegisterCustomDefinition(FirmwareDefinition definition);
    void LoadCustomDefinitions(string jsonFilePath);
}
