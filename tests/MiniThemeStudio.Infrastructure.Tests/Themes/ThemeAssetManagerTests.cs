using System.Text.Json.Nodes;
using MiniThemeStudio.Core.Audio;
using MiniThemeStudio.Core.Fonts;
using MiniThemeStudio.Core.Themes;
using MiniThemeStudio.Infrastructure.Themes;
using SkiaSharp;

namespace MiniThemeStudio.Infrastructure.Tests.Themes;

public sealed class ThemeAssetManagerTests
{
    [Fact]
    public void ExportPackedSpriteExtractsExactSubRectangle()
    {
        string tempDir = Directory.CreateTempSubdirectory().FullName;
        string atlasPath = Path.Combine(tempDir, "packed.png");
        string outputPath = Path.Combine(tempDir, "extracted.png");

        using (var bmp = new SKBitmap(10, 10))
        {
            bmp.Erase(SKColors.Black);
            bmp.SetPixel(2, 2, SKColors.Red);
            bmp.SetPixel(3, 3, SKColors.Green);
            using var img = SKImage.FromBitmap(bmp);
            using SKData data = img.Encode(SKEncodedImageFormat.Png, 100);
            using FileStream stream = File.Create(atlasPath);
            data.SaveTo(stream);
        }

        var sprite = new SpriteRegion(
            "test_sprite",
            new AtlasRectangle(2, 2, 2, 2),
            new AtlasPoint(1, 1),
            new AtlasSize(2, 2),
            new AtlasPoint(0, 0));

        try
        {
            var manager = new ThemeAssetManager();
            manager.ExportPackedSprite(atlasPath, sprite, outputPath);

            Assert.True(File.Exists(outputPath));
            using var extracted = SKBitmap.Decode(outputPath);
            Assert.Equal(2, extracted.Width);
            Assert.Equal(2, extracted.Height);
            Assert.Equal(SKColors.Red, extracted.GetPixel(0, 0));
            Assert.Equal(SKColors.Green, extracted.GetPixel(1, 1));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ReplaceWallpaperUpdatesPngAndJsonDimensions()
    {
        string tempDir = Directory.CreateTempSubdirectory().FullName;
        string wallpaperDir = Path.Combine(tempDir, "resources", "layout", "asset", "sprite", "wallpaper");
        Directory.CreateDirectory(wallpaperDir);

        string targetPng = Path.Combine(wallpaperDir, "wall.png");
        string replacementPng = Path.Combine(tempDir, "replacement.png");

        SaveSolidPng(replacementPng, 64, 32, SKColors.Blue);

        try
        {
            var manager = new ThemeAssetManager();
            manager.ReplaceWallpaper(targetPng, replacementPng);

            Assert.True(File.Exists(targetPng));
            using var loaded = SKBitmap.Decode(targetPng);
            Assert.Equal(64, loaded.Width);
            Assert.Equal(32, loaded.Height);

            JsonNode texture = ReadJson(Path.Combine(wallpaperDir, "wall.json"))["texture"]!;
            Assert.Equal(64, texture["width"]!.GetValue<int>());
            Assert.Equal(32, texture["height"]!.GetValue<int>());
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void EnsureDraftCopyKeepsBaseAtlasUntouchedWhenSpriteIsReplaced()
    {
        string tempDir = Directory.CreateTempSubdirectory().FullName;
        string draftDir = Path.Combine(tempDir, "draft");
        string baseAtlas = Path.Combine(tempDir, "base", "resources", "sprites", "packed.png");
        string replacementPng = Path.Combine(tempDir, "replacement.png");
        Directory.CreateDirectory(draftDir);
        Directory.CreateDirectory(Path.GetDirectoryName(baseAtlas)!);
        SaveSolidPng(baseAtlas, 4, 4, SKColors.Red);
        SaveSolidPng(replacementPng, 2, 2, SKColors.Blue);

        try
        {
            string draftAtlas = new ThemeAssetManager().EnsureDraftCopy(
                draftDir,
                Path.Combine("resources", "sprites", "packed.png"),
                baseAtlas);
            new TextureAtlasEditor().ReplaceSprite(new SpriteReplacementRequest(
                draftAtlas,
                new SpriteRegion("s", new AtlasRectangle(0, 0, 2, 2), new AtlasPoint(1, 1), new AtlasSize(2, 2), new AtlasPoint(0, 0)),
                replacementPng));

            Assert.StartsWith(Path.GetFullPath(draftDir), draftAtlas);
            using var draftBitmap = SKBitmap.Decode(draftAtlas);
            using var baseBitmap = SKBitmap.Decode(baseAtlas);
            Assert.Equal(SKColors.Blue, draftBitmap.GetPixel(0, 0));
            Assert.Equal(SKColors.Red, baseBitmap.GetPixel(0, 0));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void EnsureDraftCopyRejectsPathOutsideDraft()
    {
        string tempDir = Directory.CreateTempSubdirectory().FullName;
        string source = Path.Combine(tempDir, "source.png");
        SaveSolidPng(source, 1, 1, SKColors.Red);

        try
        {
            Assert.Throws<ArgumentException>(() => new ThemeAssetManager().EnsureDraftCopy(
                Path.Combine(tempDir, "draft"),
                Path.Combine("..", "escaped.png"),
                source));
            Assert.False(File.Exists(Path.Combine(tempDir, "escaped.png")));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ReplaceWallpaperFromBaseKeepsTextureIdAndLeavesBaseJsonUntouched()
    {
        string tempDir = Directory.CreateTempSubdirectory().FullName;
        string baseJson = Path.Combine(tempDir, "base", "wall.json");
        string targetPng = Path.Combine(tempDir, "draft", "wall.png");
        string replacementPng = Path.Combine(tempDir, "replacement.png");
        Directory.CreateDirectory(Path.GetDirectoryName(baseJson)!);
        const string baseContent = """{"texture":{"id":"1c75bca3-310d-48f1-95ad-20e7ebc294c6","width":512,"height":178},"sprites":{}}""";
        File.WriteAllText(baseJson, baseContent);
        SaveSolidPng(replacementPng, 512, 178, SKColors.Blue);

        try
        {
            new ThemeAssetManager().ReplaceWallpaper(targetPng, replacementPng, baseJson);

            JsonNode texture = ReadJson(Path.ChangeExtension(targetPng, ".json"))["texture"]!;
            Assert.Equal("1c75bca3-310d-48f1-95ad-20e7ebc294c6", texture["id"]!.GetValue<string>());
            Assert.Equal(baseContent, File.ReadAllText(baseJson));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ReplaceWithDifferentSizeUpdatesFullTextureSprite()
    {
        string tempDir = Directory.CreateTempSubdirectory().FullName;
        string targetPng = Path.Combine(tempDir, "goku.png");
        string replacementPng = Path.Combine(tempDir, "replacement.png");
        SaveSolidPng(targetPng, 48, 48, SKColors.Red);
        File.WriteAllText(
            Path.ChangeExtension(targetPng, ".json"),
            """{"texture":{"id":"3e60e2df","width":48,"height":48},"sprites":{"Default":{"textureFrame":[0,0,48,48],"pivot":[24,24],"sourceSize":[48,48],"sourceOffset":[0,0]}}}""");
        SaveSolidPng(replacementPng, 64, 32, SKColors.Blue);

        try
        {
            new ThemeAssetManager().ReplaceExtraAsset(targetPng, replacementPng);

            JsonNode root = ReadJson(Path.ChangeExtension(targetPng, ".json"));
            JsonNode sprite = root["sprites"]!["Default"]!;
            Assert.Equal("3e60e2df", root["texture"]!["id"]!.GetValue<string>());
            Assert.Equal("[0,0,64,32]", sprite["textureFrame"]!.ToJsonString());
            Assert.Equal("[64,32]", sprite["sourceSize"]!.ToJsonString());
            Assert.Equal("[32,16]", sprite["pivot"]!.ToJsonString());
            Assert.Equal("[0,0]", sprite["sourceOffset"]!.ToJsonString());
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ReplaceWithDifferentSizeRejectsSubSpritesAndKeepsFiles()
    {
        string tempDir = Directory.CreateTempSubdirectory().FullName;
        string targetPng = Path.Combine(tempDir, "dot_num.png");
        string targetJson = Path.ChangeExtension(targetPng, ".json");
        string replacementPng = Path.Combine(tempDir, "replacement.png");
        SaveSolidPng(targetPng, 32, 32, SKColors.Red);
        const string jsonContent = """{"texture":{"id":"4ebe20df","width":32,"height":32},"sprites":{"Sprite1":{"textureFrame":[0,0,8,8],"pivot":[4,4],"sourceSize":[8,8],"sourceOffset":[0,0]}}}""";
        File.WriteAllText(targetJson, jsonContent);
        SaveSolidPng(replacementPng, 64, 64, SKColors.Blue);

        try
        {
            Assert.Throws<InvalidDataException>(() =>
                new ThemeAssetManager().ReplaceExtraAsset(targetPng, replacementPng));

            using var kept = SKBitmap.Decode(targetPng);
            Assert.Equal(SKColors.Red, kept.GetPixel(0, 0));
            Assert.Equal(jsonContent, File.ReadAllText(targetJson));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ExtraAssetsExcludeSpriteAtlases()
    {
        string themeDir = Directory.CreateTempSubdirectory().FullName;
        string baseDir = Directory.CreateTempSubdirectory().FullName;
        string themeSprites = Path.Combine(themeDir, "resources", "sprites");
        string baseSprites = Path.Combine(baseDir, "resources", "sprites");
        Directory.CreateDirectory(Path.Combine(themeSprites, "sampleasset"));
        Directory.CreateDirectory(baseSprites);
        SaveSolidPng(Path.Combine(themeSprites, "nes.png"), 4, 4, SKColors.Red);
        SaveSolidPng(Path.Combine(baseSprites, "dot_num.png"), 4, 4, SKColors.Red);
        File.WriteAllText(
            Path.Combine(baseSprites, "dot_num.json"),
            """{"texture":{"id":"d","width":4,"height":4},"sprites":{"a":{"textureFrame":[0,0,2,2]},"b":{"textureFrame":[2,0,2,2]}}}""");
        SaveSolidPng(Path.Combine(themeSprites, "logo.png"), 4, 4, SKColors.Red);
        SaveSolidPng(Path.Combine(themeSprites, "sampleasset", "goku.png"), 4, 4, SKColors.Red);

        try
        {
            IReadOnlyList<ExtraAsset> assets = new ThemeAssetManager().GetExtraAssets(themeDir, baseDir);

            Assert.Equal(
                ["sprites/logo.png", "sprites/sampleasset/goku.png"],
                assets.Select(asset => asset.RelativePath).Order(StringComparer.Ordinal));
        }
        finally
        {
            Directory.Delete(themeDir, true);
            Directory.Delete(baseDir, true);
        }
    }

    [Fact]
    public void ExtraAssetsTellBaseReplacedAndAddedImagesApart()
    {
        string themeDir = Directory.CreateTempSubdirectory().FullName;
        string baseDir = Directory.CreateTempSubdirectory().FullName;
        string themeSprites = Path.Combine(themeDir, "resources", "sprites");
        string baseSprites = Path.Combine(baseDir, "resources", "sprites");
        Directory.CreateDirectory(themeSprites);
        Directory.CreateDirectory(baseSprites);
        SaveSolidPng(Path.Combine(baseSprites, "inherited.png"), 4, 4, SKColors.Red);
        SaveSolidPng(Path.Combine(baseSprites, "replaced.png"), 4, 4, SKColors.Red);
        SaveSolidPng(Path.Combine(themeSprites, "replaced.png"), 4, 4, SKColors.Blue);
        SaveSolidPng(Path.Combine(themeSprites, "added.png"), 4, 4, SKColors.Blue);

        try
        {
            var assets = new ThemeAssetManager().GetExtraAssets(themeDir, baseDir).ToDictionary(asset => asset.DisplayName);

            Assert.True(assets["inherited"].IsFromBaseUi);
            Assert.False(assets["inherited"].OverridesBase);
            Assert.False(assets["replaced"].IsFromBaseUi);
            Assert.True(assets["replaced"].OverridesBase);
            Assert.False(assets["added"].IsFromBaseUi);
            Assert.False(assets["added"].OverridesBase);
        }
        finally
        {
            Directory.Delete(themeDir, true);
            Directory.Delete(baseDir, true);
        }
    }

    [Fact]
    public void ProvideMissingTextureKeepsReferencedTextureId()
    {
        string draftDir = Directory.CreateTempSubdirectory().FullName;
        string sourcePng = Path.Combine(draftDir, "..", $"{Guid.NewGuid():N}.png");
        SaveSolidPng(sourcePng, 48, 48, SKColors.Green);
        var missing = new MissingTexture(
            "3e60e2df-39af-42b8-b28c-9ac3cd1a785c",
            Path.Combine("resources", "sprites", "sampleasset", "goku.png"),
            Path.Combine("resources", "sprites", "sampleasset", "goku.json"),
            ["bg.scn"]);

        try
        {
            new ThemeAssetManager().ProvideMissingTexture(draftDir, missing, sourcePng);

            JsonNode root = ReadJson(Path.Combine(draftDir, missing.DefinitionRelativePath));
            Assert.True(File.Exists(Path.Combine(draftDir, missing.ImageRelativePath)));
            Assert.Equal(missing.TextureId, root["texture"]!["id"]!.GetValue<string>());
            Assert.Equal("[0,0,48,48]", root["sprites"]!["Default"]!["textureFrame"]!.ToJsonString());
            Assert.Equal("[24,24]", root["sprites"]!["Default"]!["pivot"]!.ToJsonString());
            Assert.Throws<IOException>(() => new ThemeAssetManager().ProvideMissingTexture(draftDir, missing, sourcePng));
        }
        finally
        {
            Directory.Delete(draftDir, true);
            File.Delete(sourcePng);
        }
    }

    private static JsonNode ReadJson(string path) => JsonNode.Parse(File.ReadAllText(path))!;

    private static void SaveSolidPng(string path, int width, int height, SKColor color)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(color);
        using var image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream stream = File.Create(path);
        data.SaveTo(stream);
    }

    [Fact]
    public void GetFontsDiscoversFontsFromBaseAndDraft()
    {
        string tempBase = Directory.CreateTempSubdirectory().FullName;
        string tempDraft = Directory.CreateTempSubdirectory().FullName;

        string baseFontsDir = Path.Combine(tempBase, "resources", "fonts");
        string draftFontsDir = Path.Combine(tempDraft, "resources", "fonts");
        Directory.CreateDirectory(baseFontsDir);
        Directory.CreateDirectory(draftFontsDir);

        var font = new MiniThemeStudio.Core.Fonts.MiniFont(
            formatType: 1,
            info: new MiniThemeStudio.Core.Fonts.FontInfo("BaseL", 16, false, false, true, false, 0, 100, 0, 0, 0, 0, 0, 0, 0, 0),
            common: new MiniThemeStudio.Core.Fonts.FontCommon(16, 14, 256, 256, 1, false, 0, 0, 0, 0),
            pageNames: ["l.png"],
            glyphs: [new MiniThemeStudio.Core.Fonts.FontGlyph(65, 0, 0, 10, 14, 0, 0, 10, 0, 0)],
            kerningPairs: [],
            texturePngBytes: [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        // Put l.font in base and s.fnt in draft
        MiniThemeStudio.Infrastructure.Fonts.MiniFontWriter.Write(font, Path.Combine(baseFontsDir, "l.font"));
        MiniThemeStudio.Infrastructure.Fonts.MiniFontWriter.Write(font, Path.Combine(draftFontsDir, "s.fnt"));

        try
        {
            var manager = new ThemeAssetManager();
            IReadOnlyList<FontAsset> fonts = manager.GetFonts(tempDraft, tempBase);

            Assert.Equal(2, fonts.Count);
            FontAsset? fontL = fonts.FirstOrDefault(f => f.Name == "l");
            FontAsset? fontS = fonts.FirstOrDefault(f => f.Name == "s");

            Assert.NotNull(fontL);
            Assert.True(fontL.IsFromBaseUi);
            Assert.Equal("Large UI Font (l)", fontL.DisplayName);

            Assert.NotNull(fontS);
            Assert.False(fontS.IsFromBaseUi);
            Assert.Equal("Small UI Font (s)", fontS.DisplayName);
        }
        finally
        {
            Directory.Delete(tempBase, true);
            Directory.Delete(tempDraft, true);
        }
    }

    [Fact]
    public void GetFontsListsTheBaseTitleFontAsItIs()
    {
        string tempBase = Directory.CreateTempSubdirectory().FullName;
        string tempDraft = Directory.CreateTempSubdirectory().FullName;

        string baseFontsDir = Path.Combine(tempBase, "resources", "fonts");
        Directory.CreateDirectory(baseFontsDir);

        // Mock stock firmware title.font with only 1 glyph
        var stockFont = new MiniThemeStudio.Core.Fonts.MiniFont(
            formatType: 1,
            info: new MiniThemeStudio.Core.Fonts.FontInfo("StockTitle", 16, false, false, true, false, 0, 100, 0, 0, 0, 0, 0, 0, 0, 0),
            common: new MiniThemeStudio.Core.Fonts.FontCommon(16, 14, 256, 256, 1, false, 0, 0, 0, 0),
            pageNames: ["title.png"],
            glyphs: [new MiniThemeStudio.Core.Fonts.FontGlyph(65, 0, 0, 10, 14, 0, 0, 10, 0, 0)],
            kerningPairs: [],
            texturePngBytes: [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        MiniThemeStudio.Infrastructure.Fonts.MiniFontWriter.Write(stockFont, Path.Combine(baseFontsDir, "title.font"));

        try
        {
            var manager = new ThemeAssetManager();
            IReadOnlyList<FontAsset> fonts = manager.GetFonts(tempDraft, tempBase);

            FontAsset? titleFont = fonts.FirstOrDefault(f => f.Name == "title");
            Assert.NotNull(titleFont);
            Assert.True(titleFont.IsFromBaseUi);
            // the reference only fills characters when a font is converted
            Assert.Equal(1, titleFont.GlyphCount);
        }
        finally
        {
            Directory.Delete(tempBase, true);
            Directory.Delete(tempDraft, true);
        }
    }

    [Fact]
    public void ReplaceFontTextureUpdatesEmbeddedPngAndDimensions()
    {
        string tempBase = Directory.CreateTempSubdirectory().FullName;
        string tempDraft = Directory.CreateTempSubdirectory().FullName;
        string sourcePng = Path.GetTempFileName() + ".png";

        string baseFontsDir = Path.Combine(tempBase, "resources", "fonts");
        Directory.CreateDirectory(baseFontsDir);

        var font = new MiniThemeStudio.Core.Fonts.MiniFont(
            formatType: 1,
            info: new MiniThemeStudio.Core.Fonts.FontInfo("BaseL", 16, false, false, true, false, 0, 100, 0, 0, 0, 0, 0, 0, 0, 0),
            common: new MiniThemeStudio.Core.Fonts.FontCommon(16, 14, 256, 256, 1, false, 0, 0, 0, 0),
            pageNames: ["l.png"],
            glyphs: [new MiniThemeStudio.Core.Fonts.FontGlyph(65, 0, 0, 10, 14, 0, 0, 10, 0, 0)],
            kerningPairs: [],
            texturePngBytes: [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        string baseFontFile = Path.Combine(baseFontsDir, "l.font");
        MiniThemeStudio.Infrastructure.Fonts.MiniFontWriter.Write(font, baseFontFile);
        SaveSolidPng(sourcePng, 512, 512, SKColors.White);

        try
        {
            var manager = new ThemeAssetManager();
            FontAsset fontAsset = manager.GetFonts(tempDraft, tempBase).First(f => f.Name == "l");

            manager.ReplaceFontTexture(tempDraft, fontAsset, sourcePng);

            string draftFontFile = Path.Combine(tempDraft, "fonts", "large.font");
            Assert.True(File.Exists(draftFontFile));

            MiniFont reloaded = manager.LoadFont(draftFontFile);
            Assert.Equal(512, reloaded.Common.ScaleW);
            Assert.Equal(512, reloaded.Common.ScaleH);
        }
        finally
        {
            Directory.Delete(tempBase, true);
            Directory.Delete(tempDraft, true);
            if (File.Exists(sourcePng))
            {
                File.Delete(sourcePng);
            }
        }
    }

    [Fact]
    public void MuteBgmTrackCreatesSilentAudioAndCanBeReverted()
    {
        string tempDraft = Directory.CreateTempSubdirectory().FullName;
        string tempBase = Directory.CreateTempSubdirectory().FullName;

        try
        {
            var manager = new ThemeAssetManager();

            // Initial state: not in draft
            IReadOnlyList<BgmTrack> tracksBefore = manager.GetBgmTracks(tempDraft, tempBase);
            BgmTrack bootTrack = tracksBefore.First(t => t.Name == "bgm_boot");
            Assert.True(bootTrack.CanMute);
            Assert.False(bootTrack.IsMuted);
            Assert.False(bootTrack.ExistsInDraft);

            BgmTrack homeTrack = tracksBefore.First(t => t.Name == "bgm_home");
            Assert.False(homeTrack.CanMute);

            // Mute bgm_boot
            manager.MuteBgmTrack(tempDraft, "bgm_boot", tempBase);

            IReadOnlyList<BgmTrack> tracksMuted = manager.GetBgmTracks(tempDraft, tempBase);
            BgmTrack mutedBoot = tracksMuted.First(t => t.Name == "bgm_boot");
            Assert.True(mutedBoot.ExistsInDraft);
            Assert.True(mutedBoot.IsMuted);
            Assert.Contains("Muted", mutedBoot.StatusDescription);

            // Revert bgm_boot
            manager.RevertBgmTrack(tempDraft, "bgm_boot");

            IReadOnlyList<BgmTrack> tracksReverted = manager.GetBgmTracks(tempDraft, tempBase);
            BgmTrack revertedBoot = tracksReverted.First(t => t.Name == "bgm_boot");
            Assert.False(revertedBoot.ExistsInDraft);
            Assert.False(revertedBoot.IsMuted);
        }
        finally
        {
            Directory.Delete(tempDraft, true);
            Directory.Delete(tempBase, true);
        }
    }
}
