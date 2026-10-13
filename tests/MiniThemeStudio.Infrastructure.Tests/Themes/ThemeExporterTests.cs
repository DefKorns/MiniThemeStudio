using System.IO.Compression;
using MiniThemeStudio.Infrastructure.Themes;

namespace MiniThemeStudio.Infrastructure.Tests.Themes;

public sealed class ThemeExporterTests
{
    [Fact]
    public void ExportsThemeUnderRootFolderWithoutInternalFiles()
    {
        var rootPath = Directory.CreateTempSubdirectory().FullName;
        var themePath = Path.Combine(rootPath, "draft");
        var zipPath = Path.Combine(rootPath, "my_theme.zip");
        Directory.CreateDirectory(Path.Combine(themePath, "resources", "sprites"));
        File.WriteAllText(Path.Combine(themePath, "title.txt"), "My Theme");
        File.WriteAllText(Path.Combine(themePath, "resources", "sprites", "packed.png"), "png");
        File.WriteAllText(Path.Combine(themePath, ".mts-base.json"), "{}");
        File.WriteAllText(zipPath, "previous export");

        try
        {
            var count = new ThemeExporter().ExportZip(themePath, zipPath, "my_theme");

            using var archive = ZipFile.OpenRead(zipPath);
            Assert.Equal(2, count);
            Assert.Equal(
                ["my_theme/resources/sprites/packed.png", "my_theme/title.txt"],
                archive.Entries.Select(entry => entry.FullName).Order(StringComparer.Ordinal));
            Assert.Empty(Directory.GetFiles(rootPath, "*.tmp"));
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }
}
