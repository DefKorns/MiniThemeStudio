namespace MiniThemeStudio.Core.Console;

public interface IConsoleDirectoryClient : IAsyncDisposable
{
    Task ConnectAsync(CancellationToken cancellationToken);

    Task<bool> DirectoryExistsAsync(
        string remotePath,
        CancellationToken cancellationToken);

    Task DownloadDirectoryAsync(
        string remotePath,
        string localPath,
        CancellationToken cancellationToken);
}
