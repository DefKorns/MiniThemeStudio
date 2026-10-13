using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MiniThemeStudio.Core.Firmware;

public interface IBaseFirmwareManager
{
    string BasesRootPath { get; }
    IReadOnlyList<InstalledBaseInfo> GetInstalledBases();
    InstalledBaseInfo? GetInstalledBase(string id);
    string? FindCompatibleBaseUiPath(string? hint = null);
    Task<InstalledBaseInfo> ImportFromDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default);
    Task<InstalledBaseInfo> ImportFromArchiveAsync(string archiveFilePath, CancellationToken cancellationToken = default);
    bool IsDiscImage(string filePath, out string? reason);
}
