using System.IO;
using System.Text.Json.Nodes;
using MiniThemeStudio.Core.Themes;
using MiniThemeStudio.Infrastructure.Themes;
using SkiaSharp;
using Xunit;

namespace MiniThemeStudio.Infrastructure.Tests.Themes;

public sealed class ThemeAssetManagerTests
{
    [Fact]
    public void ExportPackedSpriteExtractsExactSubRectangle()
    {
        var tempDir = Directory.CreateTempSubdirectory().FullName;
        var atlasPath = Path.Combine(tempDir, "packed.png");
        var outputPath = Path.Combine(tempDir, "extracted.png");

        using (var bmp = new SKBitmap(10, 10))
        {
            bmp.Erase(SKColors.Black);
            bmp.SetPixel(2, 2, SKColors.Red);
            bmp.SetPixel(3, 3, SKColors.Green);
            using var img = SKImage.FromBitmap(bmp);
            using var data = img.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = File.Create(atlasPath);
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
        var tempDir = Directory.CreateTempSubdirectory().FullName;
        var wallpaperDir = Path.Combine(tempDir, "resources", "layout", "asset", "sprite", "wallpaper");
        Directory.CreateDirectory(wallpaperDir);

        var targetPng = Path.Combine(wallpaperDir, "wall.png");
        var replacementPng = Path.Combine(tempDir, "replacement.png");

        SaveSolidPng(replacementPng, 64, 32, SKColors.Blue);

        try
        {
            var manager = new ThemeAssetManager();
            manager.ReplaceWallpaper(targetPng, replacementPng);

            Assert.True(File.Exists(targetPng));
            using var loaded = SKBitmap.Decode(targetPng);
            Assert.Equal(64, loaded.Width);
            Assert.Equal(32, loaded.Height);

            var texture = ReadJson(Path.Combine(wallpaperDir, "wall.json"))["texture"]!;
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
        var tempDir = Directory.CreateTempSubdirectory().FullName;
        var draftDir = Path.Combine(tempDir, "draft");
        var baseAtlas = Path.Combine(tempDir, "base", "resources", "sprites", "packed.png");
        var replacementPng = Path.Combine(tempDir, "replacement.png");
        Directory.CreateDirectory(draftDir);
        Directory.CreateDirectory(Path.GetDirectoryName(baseAtlas)!);
        SaveSolidPng(baseAtlas, 4, 4, SKColors.Red);
        SaveSolidPng(replacementPng, 2, 2, SKColors.Blue);

        try
        {
            var draftAtlas = new ThemeAssetManager().EnsureDraftCopy(
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
        var tempDir = Directory.CreateTempSubdirectory().FullName;
        var source = Path.Combine(tempDir, "source.png");
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
        var tempDir = Directory.CreateTempSubdirectory().FullName;
        var baseJson = Path.Combine(tempDir, "base", "wall.json");
        var targetPng = Path.Combine(tempDir, "draft", "wall.png");
        var replacementPng = Path.Combine(tempDir, "replacement.png");
        Directory.CreateDirectory(Path.GetDirectoryName(baseJson)!);
        const string baseContent = """{"texture":{"id":"1c75bca3-310d-48f1-95ad-20e7ebc294c6","width":512,"height":178},"sprites":{}}""";
        File.WriteAllText(baseJson, baseContent);
        SaveSolidPng(replacementPng, 512, 178, SKColors.Blue);

        try
        {
            new ThemeAssetManager().ReplaceWallpaper(targetPng, replacementPng, baseJson);

            var texture = ReadJson(Path.ChangeExtension(targetPng, ".json"))["texture"]!;
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
        var tempDir = Directory.CreateTempSubdirectory().FullName;
        var targetPng = Path.Combine(tempDir, "goku.png");
        var replacementPng = Path.Combine(tempDir, "replacement.png");
        SaveSolidPng(targetPng, 48, 48, SKColors.Red);
        File.WriteAllText(
            Path.ChangeExtension(targetPng, ".json"),
            """{"texture":{"id":"3e60e2df","width":48,"height":48},"sprites":{"Default":{"textureFrame":[0,0,48,48],"pivot":[24,24],"sourceSize":[48,48],"sourceOffset":[0,0]}}}""");
        SaveSolidPng(replacementPng, 64, 32, SKColors.Blue);

        try
        {
            new ThemeAssetManager().ReplaceExtraAsset(targetPng, replacementPng);

            var root = ReadJson(Path.ChangeExtension(targetPng, ".json"));
            var sprite = root["sprites"]!["Default"]!;
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
        var tempDir = Directory.CreateTempSubdirectory().FullName;
        var targetPng = Path.Combine(tempDir, "dot_num.png");
        var targetJson = Path.ChangeExtension(targetPng, ".json");
        var replacementPng = Path.Combine(tempDir, "replacement.png");
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

    private static JsonNode ReadJson(string path) => JsonNode.Parse(File.ReadAllText(path))!;

    private static void SaveSolidPng(string path, int width, int height, SKColor color)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.SaveTo(stream);
    }
}
