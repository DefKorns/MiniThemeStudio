using MiniThemeStudio.Core.Console;
using MiniThemeStudio.Core.Themes;

namespace MiniThemeStudio.Infrastructure.Console;

public sealed class ConsoleBaseUiImporter(
    IBaseUiValidator validator,
    IConsoleDirectoryClientFactory clientFactory) : IConsoleBaseUiImporter
{
    public async Task<BaseUiImportResult> ImportAvailableAsync(
        string baseUiRootPath,
        ConsoleConnectionOptions connection,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUiRootPath);
        ArgumentNullException.ThrowIfNull(connection);

        await using var client = clientFactory.Create(connection);
        await client.ConnectAsync(cancellationToken);
        var definition = await FindAvailableDefinitionAsync(client, cancellationToken);
        var remotePath = await FindRemotePathAsync(client, definition, cancellationToken);

        return await DownloadAndPublishAsync(
            client,
            definition,
            remotePath,
            baseUiRootPath,
            cancellationToken);
    }

    public async Task<BaseUiImportResult> ImportAsync(
        BaseUiDefinition definition,
        string baseUiRootPath,
        ConsoleConnectionOptions connection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUiRootPath);
        ArgumentNullException.ThrowIfNull(connection);

        await using var client = clientFactory.Create(connection);
        await client.ConnectAsync(cancellationToken);
        var remotePath = await FindRemotePathAsync(client, definition, cancellationToken);

        return await DownloadAndPublishAsync(
            client,
            definition,
            remotePath,
            baseUiRootPath,
            cancellationToken);
    }

    private async Task<BaseUiImportResult> DownloadAndPublishAsync(
        IConsoleDirectoryClient client,
        BaseUiDefinition definition,
        string remotePath,
        string baseUiRootPath,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(baseUiRootPath);
        var destinationPath = Path.Combine(baseUiRootPath, definition.LocalFolderName);
        var stagingPath = Path.Combine(
            baseUiRootPath,
            $".{definition.LocalFolderName}.mts-stage-{Guid.NewGuid():N}");
        var backupPath = Path.Combine(
            baseUiRootPath,
            $".{definition.LocalFolderName}.mts-backup-{Guid.NewGuid():N}");
        var destinationMoved = false;

        try
        {
            Directory.CreateDirectory(stagingPath);
            await client.DownloadDirectoryAsync(remotePath, stagingPath, cancellationToken);
            validator.Validate(stagingPath);

            if (Directory.Exists(destinationPath))
            {
                Directory.Move(destinationPath, backupPath);
                destinationMoved = true;
            }

            Directory.Move(stagingPath, destinationPath);
            DeleteDirectory(backupPath);
            return new BaseUiImportResult(definition, destinationPath, remotePath);
        }
        catch
        {
            DeleteDirectory(stagingPath);

            if (destinationMoved && !Directory.Exists(destinationPath))
            {
                Directory.Move(backupPath, destinationPath);
            }

            throw;
        }
    }

    private static async Task<BaseUiDefinition> FindAvailableDefinitionAsync(
        IConsoleDirectoryClient client,
        CancellationToken cancellationToken)
    {
        foreach (var definition in BaseUiCatalog.All)
        {
            if (await client.DirectoryExistsAsync(
                definition.OriginalConsolePath,
                cancellationToken))
            {
                return definition;
            }
        }

        foreach (var definition in BaseUiCatalog.All)
        {
            if (await client.DirectoryExistsAsync(definition.ConsolePath, cancellationToken))
            {
                return definition;
            }
        }

        throw new DirectoryNotFoundException(
            "No supported Base UI was found on the connected console.");
    }

    private static async Task<string> FindRemotePathAsync(
        IConsoleDirectoryClient client,
        BaseUiDefinition definition,
        CancellationToken cancellationToken)
    {
        if (await client.DirectoryExistsAsync(
            definition.OriginalConsolePath,
            cancellationToken))
        {
            return definition.OriginalConsolePath;
        }

        if (await client.DirectoryExistsAsync(definition.ConsolePath, cancellationToken))
        {
            return definition.ConsolePath;
        }

        throw new DirectoryNotFoundException(
            $"Base UI was not found at {definition.OriginalConsolePath} or {definition.ConsolePath}.");
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }
    }
}
