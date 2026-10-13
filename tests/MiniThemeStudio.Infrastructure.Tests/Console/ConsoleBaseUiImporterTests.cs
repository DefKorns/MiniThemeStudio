using MiniThemeStudio.Core.Console;
using MiniThemeStudio.Infrastructure.Console;
using MiniThemeStudio.Infrastructure.Themes;

namespace MiniThemeStudio.Infrastructure.Tests.Console;

public sealed class ConsoleBaseUiImporterTests
{
    [Fact]
    public async Task DetectsOriginalUiAndPublishesValidatedDownload()
    {
        var rootPath = Directory.CreateTempSubdirectory().FullName;
        var client = new FakeConsoleDirectoryClient(
            "/var/squashfs/usr/share/ui/snes-eur",
            createValidDownload: true);
        var importer = new ConsoleBaseUiImporter(
            new BaseUiValidator(),
            new FakeClientFactory(client));

        try
        {
            var result = await importer.ImportAvailableAsync(
                rootPath,
                CreateConnection());

            Assert.Equal("sneseur", result.Definition.Id);
            Assert.Equal(
                "/var/squashfs/usr/share/ui/snes-eur",
                result.RemotePath);
            Assert.True(File.Exists(Path.Combine(result.LocalPath, "init.json")));
            Assert.Equal(1, client.ConnectCount);
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    [Fact]
    public async Task InvalidDownloadDoesNotReplaceExistingBaseUi()
    {
        var rootPath = Directory.CreateTempSubdirectory().FullName;
        var destinationPath = Path.Combine(rootPath, "sneseur");
        Directory.CreateDirectory(destinationPath);
        var markerPath = Path.Combine(destinationPath, "existing.txt");
        File.WriteAllText(markerPath, "preserved");
        var client = new FakeConsoleDirectoryClient(
            "/var/squashfs/usr/share/ui/snes-eur",
            createValidDownload: false);
        var importer = new ConsoleBaseUiImporter(
            new BaseUiValidator(),
            new FakeClientFactory(client));

        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                importer.ImportAvailableAsync(rootPath, CreateConnection()));

            Assert.Equal("preserved", File.ReadAllText(markerPath));
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    private static ConsoleConnectionOptions CreateConnection()
    {
        return new ConsoleConnectionOptions("169.254.13.37");
    }

    private sealed class FakeClientFactory(IConsoleDirectoryClient client)
        : IConsoleDirectoryClientFactory
    {
        public IConsoleDirectoryClient Create(ConsoleConnectionOptions options)
        {
            return client;
        }
    }

    private sealed class FakeConsoleDirectoryClient(
        string availablePath,
        bool createValidDownload) : IConsoleDirectoryClient
    {
        public int ConnectCount { get; private set; }

        public Task ConnectAsync(CancellationToken cancellationToken)
        {
            ConnectCount++;
            return Task.CompletedTask;
        }

        public Task<bool> DirectoryExistsAsync(
            string remotePath,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(remotePath == availablePath);
        }

        public Task DownloadDirectoryAsync(
            string remotePath,
            string localPath,
            CancellationToken cancellationToken)
        {
            File.WriteAllText(Path.Combine(localPath, "init.json"), "{}");

            if (createValidDownload)
            {
                File.WriteAllText(Path.Combine(localPath, "dependencies.json"), "{}");
                Directory.CreateDirectory(Path.Combine(localPath, "resources"));
            }

            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
