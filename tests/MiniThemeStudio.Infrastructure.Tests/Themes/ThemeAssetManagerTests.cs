using System.IO;
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

            var jsonPath = Path.Combine(wallpaperDir, "wall.json");
            Assert.True(File.Exists(jsonPath));
            var jsonContent = File.ReadAllText(jsonPath);
            Assert.Contains("\"width\": 64", jsonContent);
            Assert.Contains("\"height\": 32", jsonContent);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

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
