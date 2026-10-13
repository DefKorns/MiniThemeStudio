using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using MiniThemeStudio.Core.Firmware;
using MiniThemeStudio.Infrastructure.Firmware;
using MiniThemeStudio.Infrastructure.Themes;
using Xunit;

namespace MiniThemeStudio.Infrastructure.Tests.Firmware;

public sealed class BaseFirmwareManagerTests
{
    [Fact]
    public async Task ExtractsAndImportsValidBaseFromTarGz()
    {
        var tempDir = Directory.CreateTempSubdirectory().FullName;
        var basesDir = Path.Combine(tempDir, "bases");
        var tarGzPath = Path.Combine(tempDir, "dp-sneseur-release-v2.0.14-0-gd8b65c6.tar.gz");

        CreateSampleTarGzWithUi(tarGzPath, "usr/share/ui/snes-eur", "/scripts/env/env_eur.lua");

        try
        {
            var registry = new FirmwareRegistry();
            var manager = new BaseFirmwareManager(registry, new BaseUiValidator(), basesDir);

            var result = await manager.ImportFromArchiveAsync(tarGzPath);

            Assert.Equal(BaseInstallStatus.Valid, result.Status);
            Assert.Equal("dp-sneseur-v2.0.14", result.Definition.Id);
            Assert.True(File.Exists(Path.Combine(result.LocalPath, "init.json")));
            Assert.True(File.Exists(Path.Combine(result.LocalPath, "dependencies.json")));
            Assert.True(Directory.Exists(Path.Combine(result.LocalPath, "resources")));

            // Prevent duplicate extraction
            var secondResult = await manager.ImportFromArchiveAsync(tarGzPath);
            Assert.Equal(result.LocalPath, secondResult.LocalPath);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void DetectsSquashFsImagesWithHelpfulReason()
    {
        var tempDir = Directory.CreateTempSubdirectory().FullName;
        var hsqsPath = Path.Combine(tempDir, "dp-sneseur-release-v2.0.14-0-gd8b65c6.hsqs");
        File.WriteAllBytes(hsqsPath, [0x68, 0x73, 0x71, 0x73, 0x00, 0x00]);

        try
        {
            var manager = new BaseFirmwareManager(new FirmwareRegistry(), basesRootPath: tempDir);
            var isDisc = manager.IsDiscImage(hsqsPath, out var reason);
            Assert.True(isDisc);
            Assert.NotNull(reason);
            Assert.Contains("tar.gz", reason);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    private static void CreateSampleTarGzWithUi(string destinationTarGz, string uiPrefix, string script)
    {
        var tempContentDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var fullUiDir = Path.Combine(tempContentDir, uiPrefix);
            var resDir = Path.Combine(fullUiDir, "resources");
            Directory.CreateDirectory(resDir);
            File.WriteAllText(Path.Combine(resDir, "res.txt"), "sample resource");
            File.WriteAllText(Path.Combine(fullUiDir, "dependencies.json"), "{}");
            File.WriteAllText(
                Path.Combine(fullUiDir, "init.json"),
                $$"""{ "engineFormat": 9, "luaScripts": ["{{script}}"] }""");

            using var fileStream = File.Create(destinationTarGz);
            using var gzipStream = new GZipStream(fileStream, CompressionMode.Compress);
            using var tarWriter = new TarWriter(gzipStream);

            foreach (var dir in Directory.EnumerateDirectories(tempContentDir, "*", SearchOption.AllDirectories))
            {
                var relDir = Path.GetRelativePath(tempContentDir, dir).Replace('\\', '/') + "/";
                tarWriter.WriteEntry(dir, relDir);
            }

            foreach (var file in Directory.EnumerateFiles(tempContentDir, "*", SearchOption.AllDirectories))
            {
                var relName = Path.GetRelativePath(tempContentDir, file).Replace('\\', '/');
                tarWriter.WriteEntry(file, relName);
            }
        }
        finally
        {
            Directory.Delete(tempContentDir, true);
        }
    }
}
