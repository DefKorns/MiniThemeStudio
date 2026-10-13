using MiniThemeStudio.Infrastructure.Themes;

namespace MiniThemeStudio.Infrastructure.Tests.Themes;

public sealed class LocalBaseUiImporterTests
{
    [Fact]
    public void ImportsValidBaseUiIntoApplicationCache()
    {
        var sourcePath = Directory.CreateTempSubdirectory().FullName;
        var rootPath = Directory.CreateTempSubdirectory().FullName;
        CreateValidBaseUi(sourcePath, "/scripts/colors_nes.lua", 8);
        File.WriteAllText(Path.Combine(sourcePath, "marker.txt"), "original");

        try
        {
            var destinationPath = new LocalBaseUiImporter(new BaseUiValidator())
                .Import(sourcePath, rootPath);
            File.WriteAllText(Path.Combine(sourcePath, "marker.txt"), "changed");

            Assert.Equal("nes", Path.GetFileName(destinationPath));
            Assert.Equal("original", File.ReadAllText(Path.Combine(destinationPath, "marker.txt")));
        }
        finally
        {
            Directory.Delete(sourcePath, true);
            Directory.Delete(rootPath, true);
        }
    }

    [Theory]
    [InlineData("/scripts/env/env_eur.lua", 9, "snes-eur")]
    [InlineData("/scripts/env/env_usa.lua", 9, "snes-usa")]
    [InlineData("/scripts/env/env_jpn.lua", 9, "snes-jpn")]
    [InlineData("/scripts/colors_nes.lua", 8, "nes")]
    [InlineData("/scripts/colors_hvc.lua", 10, "shonen")]
    public void UsesDetectedConsoleTypeAsCacheFolder(
        string script,
        int engineFormat,
        string expectedFolderName)
    {
        var sourcePath = Directory.CreateTempSubdirectory().FullName;
        var rootPath = Directory.CreateTempSubdirectory().FullName;
        CreateValidBaseUi(sourcePath, script, engineFormat);

        try
        {
            var destinationPath = new LocalBaseUiImporter(new BaseUiValidator())
                .Import(sourcePath, rootPath);

            Assert.Equal(expectedFolderName, Path.GetFileName(destinationPath));
        }
        finally
        {
            Directory.Delete(sourcePath, true);
            Directory.Delete(rootPath, true);
        }
    }

    private static void CreateValidBaseUi(string path, string script, int engineFormat)
    {
        Directory.CreateDirectory(Path.Combine(path, "resources"));
        File.WriteAllText(Path.Combine(path, "dependencies.json"), "{}");
        File.WriteAllText(
            Path.Combine(path, "init.json"),
            $$"""
            { "engineFormat": {{engineFormat}}, "luaScripts": ["{{script}}"] }
            """);
    }
}
