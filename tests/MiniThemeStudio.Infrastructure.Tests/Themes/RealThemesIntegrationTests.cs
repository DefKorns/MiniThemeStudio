using MiniThemeStudio.Core.Firmware;
using MiniThemeStudio.Core.Themes;
using MiniThemeStudio.Infrastructure.Firmware;
using MiniThemeStudio.Infrastructure.Themes;

namespace MiniThemeStudio.Infrastructure.Tests.Themes;

public sealed class RealThemesIntegrationTests
{
    [Theory]
    [InlineDataWhenPresent(@"Z:\_organize\minis\nes\dp_hvcj_release_v3.0.1_0_gad315e1_trimmed.hsqs", "shonen-jpn-3.0.1")]
    [InlineDataWhenPresent(NesUsaImage, "nes-usa-eur-1.0.2")]
    [InlineDataWhenPresent(@"Z:\_organize\minis\snes\dp-sneseur-release-v2.0.14-0-gd8b65c6.tar.gz", "snes-eur-2.0.13")]
    public async Task ImportsRealFirmwareFilesIfPresent(string firmwarePath, string expectedId)
    {
        string tempBases = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var manager = new BaseFirmwareManager(FirmwareRegistry.CreateDefault(), basesRootPath: tempBases);

            InstalledBaseInfo result = await manager.ImportFromFileAsync(firmwarePath);

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

    [FactWhenPresent(NesUsaImage)]
    public async Task ReadsRealNesAtlasesIfPresent()
    {
        string firmwarePath = NesUsaImage;
        string tempBases = Directory.CreateTempSubdirectory().FullName;
        string emptyTheme = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var manager = new BaseFirmwareManager(FirmwareRegistry.CreateDefault(), basesRootPath: tempBases);
            InstalledBaseInfo nesBase = await manager.ImportFromFileAsync(firmwarePath);

            IReadOnlyList<TextureAtlas> atlases = new TextureAtlasReader().ReadAll(emptyTheme, nesBase.LocalPath);

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

    [FactWhenPresent(NesDataTar)]
    public async Task RejectsNesDataTarIfPresent()
    {
        string dataTar = NesDataTar;
        string tempBases = Directory.CreateTempSubdirectory().FullName;
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

    [TheoryWhenPresent(SnesEuropeImage)]
    [InlineDataWhenPresent(SnesThemes + "shonen_jump_50th_ui")]
    [InlineDataWhenPresent(SnesThemes + "chrono_trigger")]
    [InlineDataWhenPresent(SnesThemes + "wii_theme")]
    public async Task RealSnesThemesValidateAgainstRealBaseIfPresent(string themePath)
    {
        string tempBases = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var manager = new BaseFirmwareManager(FirmwareRegistry.CreateDefault(), basesRootPath: tempBases);
            InstalledBaseInfo snesBase = await manager.ImportFromFileAsync(SnesEuropeImage);

            ThemeValidationResult validation = new ThemeValidator().Validate(themePath, snesBase.LocalPath);
            TextureAtlas atlas = new TextureAtlasReader().Read(themePath, snesBase.LocalPath)!;

            Assert.True(validation.IsValid, string.Join("\n", validation.Issues.Where(issue => issue.IsError).Select(issue => issue.Message)));
            Assert.Equal("packed", atlas.Name);
            Assert.NotEmpty(new ThemeAssetManager().GetWallpapers(themePath, snesBase.LocalPath));
        }
        finally
        {
            Directory.Delete(tempBases, true);
        }
    }

    [FactWhenPresent(SnesThemes + "shonen_jump_50th_ui", SnesEuropeImage)]
    public async Task ShonenCharactersAreUsedByItsBackgroundSceneIfPresent()
    {
        string themePath = SnesThemes + "shonen_jump_50th_ui";
        string tempBases = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var manager = new BaseFirmwareManager(FirmwareRegistry.CreateDefault(), basesRootPath: tempBases);
            InstalledBaseInfo snesBase = await manager.ImportFromFileAsync(SnesEuropeImage);

            ThemeResourceReport report = new ThemeResourceIndexer().Analyze(themePath, snesBase.LocalPath);
            ExtraAsset goku = Assert.Single(new ThemeAssetManager().GetExtraAssets(themePath, snesBase.LocalPath), asset => asset.DisplayName == "01_goku_48x48_v2");

            Assert.Equal(["bg.scn"], report.UsageOf(report.TextureIdOf(goku.RelativePath, goku.TextureId)));
            Assert.Empty(report.MissingTextures);
        }
        finally
        {
            Directory.Delete(tempBases, true);
        }
    }

    [FactWhenPresent(OldPackedPng, SnesEuropeImage, NesUsaImage)]
    public async Task OldPackedPngFitsSnesLayoutBestIfPresent()
    {
        string packedPath = OldPackedPng;
        string nesImage = NesUsaImage;
        string tempBases = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var manager = new BaseFirmwareManager(FirmwareRegistry.CreateDefault(), basesRootPath: tempBases);
            InstalledBaseInfo[] bases = new[] { await manager.ImportFromFileAsync(nesImage), await manager.ImportFromFileAsync(SnesEuropeImage) };

            IReadOnlyList<AtlasLayoutMatch> matches = new AtlasLayoutMatcher().Rank(packedPath, bases);

            Assert.Equal("snes-eur-2.0.13", matches[0].Base.Definition.Id);
            Assert.True(matches[0].CoveragePercent > 95);
            Assert.True(matches[0].CoveragePercent > matches[1].CoveragePercent + 5);
        }
        finally
        {
            Directory.Delete(tempBases, true);
        }
    }

    [FactWhenPresent(SnesEuropeImage)]
    public async Task SixFrameRunAnimationValidatesOnRealSnesBaseIfPresent()
    {
        string tempRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var manager = new BaseFirmwareManager(FirmwareRegistry.CreateDefault(), basesRootPath: Path.Combine(tempRoot, "bases"));
            InstalledBaseInfo snesBase = await manager.ImportFromFileAsync(SnesEuropeImage);
            string draft = Directory.CreateDirectory(Path.Combine(tempRoot, "draft")).FullName;
            var editor = new CharacterAnimationEditor();
            CharacterAnimation run = editor.GetAnimations(draft, snesBase.LocalPath, "mario").Single(animation => animation.Name == "run");
            AnimationTrack track = run.Tracks.Single(candidate => candidate.NodeName == "Sprite_Mario_0");

            var frameIds = new List<string>();
            for (int index = 0; index < 6; index++)
            {
                string framePath = Path.Combine(tempRoot, $"frame{index}.png");
                CharacterAnimationEditorTests.SavePng(framePath, 35, 43);
                frameIds.Add(editor.AddTexture(draft, snesBase.LocalPath, $"resources/layout/asset/sprite/mario/samus_run{index + 1:00}.png", framePath));
            }

            var keys = frameIds.Select((id, index) => new AnimationKey(index * 0.025, id))
                .Concat(track.Keys.Where(key => key.Time >= run.Duration))
                .ToList();
            editor.SetTrackKeys(draft, snesBase.LocalPath, run.ResourcePath, "Sprite_Mario_0", keys, 0.15);
            editor.SetFrameSize(draft, snesBase.LocalPath, "mario", new AtlasSize(35, 43));

            ThemeResourceReport report = new ThemeResourceIndexer().Analyze(draft, snesBase.LocalPath);
            ThemeValidationResult validation = new ThemeValidator().Validate(draft, snesBase.LocalPath);
            Assert.True(validation.IsValid, string.Join("\n", validation.Issues.Where(issue => issue.IsError).Select(issue => issue.Message)));
            Assert.All(frameIds, id => Assert.Equal(["lyt_mario@run.sceneanimation"], report.UsageOf(id)));
            Assert.All(editor.GetNodes(draft, snesBase.LocalPath, "mario"), node => Assert.Equal((105, 129), (node.Width, node.Height)));
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    private const string SnesEuropeImage = @"Z:\_organize\minis\snes\dp-sneseur-release-v2.0.14-0-gd8b65c6.hsqs";
    private const string NesUsaImage = @"Z:\_organize\minis\nes\dp-nes-release-v1.0.7-0-g4ea4041.hsqs";
    private const string NesDataTar = @"Z:\_organize\minis\nes\dp-nes-release-v1.0.7-0-g4ea4041.tar";
    private const string SnesThemes = @"E:\Documents\_projects\hmods\om_themes\new-packaging\snes\themes\";
    private const string OldPackedPng = @"E:\Documents\_projects\hmods\om_themes\wip\packed.png";
}
