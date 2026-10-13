using MiniThemeStudio.Core.Console;

namespace MiniThemeStudio.Core.Firmware;

public interface IBaseFirmwareManager
{
    string BasesRootPath { get; }

    IReadOnlyList<InstalledBaseInfo> GetInstalledBases();

    string? FindCompatibleBaseUiPath(string? themePath = null);

    Task<InstalledBaseInfo> ImportFromFileAsync(string filePath, CancellationToken cancellationToken = default);

    Task<InstalledBaseInfo> ImportFromDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default);

    Task<InstalledBaseInfo> ImportFromConsoleAsync(ConsoleConnectionOptions connection, CancellationToken cancellationToken = default);
}
