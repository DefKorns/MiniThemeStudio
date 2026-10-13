using System.IO;
using System.Threading.Tasks;
using MiniThemeStudio.Core.Firmware;
using MiniThemeStudio.Core.Themes;
using MiniThemeStudio.Infrastructure.Firmware;
using MiniThemeStudio.Infrastructure.Themes;
using Xunit;

namespace MiniThemeStudio.Infrastructure.Tests.Themes;

public sealed class RealThemesIntegrationTests
{
    [Theory]
    [InlineData(@"Z:\_organize\minis\nes\dp_hvcj_release_v3.0.1_0_gad315e1_trimmed.hsqs", "shonen-jpn-3.0.1")]
    [InlineData(@"Z:\_organize\minis\nes\dp-nes-release-v1.0.7-0-g4ea4041.hsqs", "nes-usa-eur-1.0.2")]
    [InlineData(@"Z:\_organize\minis\snes\dp-sneseur-release-v2.0.14-0-gd8b65c6.tar.gz", "snes-eur-2.0.13")]
    public async Task ImportsRealFirmwareFilesIfPresent(string firmwarePath, string expectedId)
    {
        if (!File.Exists(firmwarePath))
        {
            return;
        }

        var tempBases = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var manager = new BaseFirmwareManager(FirmwareRegistry.CreateDefault(), basesRootPath: tempBases);

            var result = await manager.ImportFromFileAsync(firmwarePath);

            Assert.Equal(BaseInstallStatus.Valid, result.Status);
            Assert.Equal(expectedId, result.Definition.Id);
            Assert.True(File.Exists(Path.Combine(result.LocalPath, "init.json")));
            Assert.Equal(
                result.Definition.UiTreeSha256,
                UiTreeHash.ComputeForDirectory(result.LocalPath).Sha256);
        }
        finally
        {
            Directory.Delete(tempBases, true);
        }
    }

    [Fact]
    public async Task ReadsRealNesAtlasesIfPresent()
    {
        var firmwarePath = @"Z:\_organize\minis\nes\dp-nes-release-v1.0.7-0-g4ea4041.hsqs";
        if (!File.Exists(firmwarePath))
        {
            return;
        }

        var tempBases = Directory.CreateTempSubdirectory().FullName;
        var emptyTheme = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var manager = new BaseFirmwareManager(FirmwareRegistry.CreateDefault(), basesRootPath: tempBases);
            var nesBase = await manager.ImportFromFileAsync(firmwarePath);

            var atlases = new TextureAtlasReader().ReadAll(emptyTheme, nesBase.LocalPath);

            Assert.Equal("nes", atlases[0].Name);
            Assert.Equal(170, atlases[0].Sprites.Count);
            Assert.Contains(atlases, atlas => atlas.Name == "dot_num");
            Assert.True(new ThemeValidator().Validate(nesBase.LocalPath).IsValid);
        }
        finally
        {
            Directory.Delete(tempBases, true);
            Directory.Delete(emptyTheme, true);
        }
    }

    [Fact]
    public async Task RejectsNesDataTarIfPresent()
    {
        var dataTar = @"Z:\_organize\minis\nes\dp-nes-release-v1.0.7-0-g4ea4041.tar";
        if (!File.Exists(dataTar))
        {
            return;
        }

        var tempBases = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var manager = new BaseFirmwareManager(FirmwareRegistry.CreateDefault(), basesRootPath: tempBases);

            await Assert.ThrowsAsync<InvalidDataException>(() => manager.ImportFromFileAsync(dataTar));
            Assert.All(manager.GetInstalledBases(), info => Assert.Equal(BaseInstallStatus.Missing, info.Status));
        }
        finally
        {
            Directory.Delete(tempBases, true);
        }
    }

    [Theory]
    [InlineData("shonen_jump_50th_ui")]
    [InlineData("chrono_trigger")]
    [InlineData("wii_theme")]
    public async Task RealSnesThemesValidateAgainstRealBaseIfPresent(string themeName)
    {
        var themePath = Path.Combine(@"E:\Documents\_projects\hmods\om_themes\new-packaging\snes\themes", themeName);
        if (!Directory.Exists(themePath) || !File.Exists(SnesEuropeImage))
        {
            return;
        }

        var tempBases = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var manager = new BaseFirmwareManager(FirmwareRegistry.CreateDefault(), basesRootPath: tempBases);
            var snesBase = await manager.ImportFromFileAsync(SnesEuropeImage);

            var validation = new ThemeValidator().Validate(themePath, snesBase.LocalPath);
            var atlas = new TextureAtlasReader().Read(themePath, snesBase.LocalPath)!;

            Assert.True(validation.IsValid, string.Join("\n", validation.Issues.Where(issue => issue.IsError).Select(issue => issue.Message)));
            Assert.Equal("packed", atlas.Name);
            Assert.NotEmpty(new ThemeAssetManager().GetWallpapers(themePath, snesBase.LocalPath));
        }
        finally
        {
            Directory.Delete(tempBases, true);
        }
    }

    [Fact]
    public async Task ShonenCharactersAreUsedByItsBackgroundSceneIfPresent()
    {
        var themePath = @"E:\Documents\_projects\hmods\om_themes\new-packaging\snes\themes\shonen_jump_50th_ui";
        if (!Directory.Exists(themePath) || !File.Exists(SnesEuropeImage))
        {
            return;
        }

        var tempBases = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var manager = new BaseFirmwareManager(FirmwareRegistry.CreateDefault(), basesRootPath: tempBases);
            var snesBase = await manager.ImportFromFileAsync(SnesEuropeImage);

            var report = new ThemeResourceIndexer().Analyze(themePath, snesBase.LocalPath);
            var goku = Assert.Single(new ThemeAssetManager().GetExtraAssets(themePath, snesBase.LocalPath), asset => asset.DisplayName == "01_goku_48x48_v2");

            Assert.Equal("Characters", goku.Category);
            Assert.Equal(["bg.scn"], report.UsageOf(goku.TextureId));
            Assert.Empty(report.MissingTextures);
        }
        finally
        {
            Directory.Delete(tempBases, true);
        }
    }

    private const string SnesEuropeImage = @"Z:\_organize\minis\snes\dp-sneseur-release-v2.0.14-0-gd8b65c6.hsqs";
}
