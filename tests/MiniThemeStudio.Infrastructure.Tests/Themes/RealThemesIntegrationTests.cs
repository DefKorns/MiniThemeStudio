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

    [Fact]
    public void CanInspectRealShonenThemeWallpapersAndExtraAssets()
    {
        var shonenPath = @"E:\Documents\_projects\hmods\om_themes\new-packaging\snes\themes\shonen_jump_50th_ui";
        if (!Directory.Exists(shonenPath))
        {
            return;
        }

        var assetManager = new ThemeAssetManager();
        var extraAssets = assetManager.GetExtraAssets(shonenPath);
        Assert.NotEmpty(extraAssets);

        // Shonen contains characters in sampleasset/
        Assert.Contains(extraAssets, a => a.Category == "Characters");

        var validator = new ThemeValidator();
        var baseUiPath = @"Z:\_organize\minis\snes\snes-eur";
        var validation = validator.Validate(shonenPath, Directory.Exists(baseUiPath) ? baseUiPath : null);
        Assert.True(validation.IsValid);
    }

    [Fact]
    public void CanInspectRealChronoTriggerTheme()
    {
        var chronoPath = @"E:\Documents\_projects\hmods\om_themes\new-packaging\snes\themes\chrono_trigger";
        if (!Directory.Exists(chronoPath))
        {
            return;
        }

        var assetManager = new ThemeAssetManager();
        var wallpapers = assetManager.GetWallpapers(chronoPath);
        Assert.NotEmpty(wallpapers);

        var extraAssets = assetManager.GetExtraAssets(chronoPath);
        Assert.NotEmpty(extraAssets);

        var validator = new ThemeValidator();
        var validation = validator.Validate(chronoPath);
        Assert.True(validation.IsValid);
    }

    [Fact]
    public void CanInspectIncompleteThemeDeloreanWithFallbackBase()
    {
        var deloreanPath = @"Z:\_organize\minis\snes\theme\delorean";
        var baseUiPath = @"Z:\_organize\minis\snes\snes-eur";
        if (!Directory.Exists(deloreanPath) || !Directory.Exists(baseUiPath))
        {
            return;
        }

        var assetManager = new ThemeAssetManager();
        var wallpapers = assetManager.GetWallpapers(deloreanPath, baseUiPath);
        Assert.NotEmpty(wallpapers);

        // DeLorean has custom pole wallpapers
        Assert.Contains(wallpapers, w => w.Name.StartsWith("pole", System.StringComparison.OrdinalIgnoreCase));

        var validator = new ThemeValidator();
        var validation = validator.Validate(deloreanPath, baseUiPath);
        Assert.True(validation.IsValid);
    }
}
