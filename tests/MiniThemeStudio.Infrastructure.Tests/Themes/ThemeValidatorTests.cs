using MiniThemeStudio.Infrastructure.Themes;
using SkiaSharp;

namespace MiniThemeStudio.Infrastructure.Tests.Themes;

public sealed class ThemeValidatorTests
{
    [Fact]
    public void ValidatesAtlasMatchingCoordinates()
    {
        var tempDir = Directory.CreateTempSubdirectory().FullName;
        var spritesDir = Path.Combine(tempDir, "resources", "sprites");
        Directory.CreateDirectory(spritesDir);

        SaveSolidPng(Path.Combine(spritesDir, "packed.png"), 100, 100, SKColors.Red);
        File.WriteAllText(
            Path.Combine(spritesDir, "packed.json"),
            """
            {
              "texture": { "id": "test", "width": 100, "height": 100 },
              "sprites": {
                "icon": { "textureFrame": [10, 10, 20, 20], "pivot": [0.5, 0.5] }
              }
            }
            """);

        try
        {
            var validator = new ThemeValidator();
            var result = validator.Validate(tempDir);
            Assert.True(result.IsValid);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void RejectsAtlasWithOutOfBoundsSpriteCoordinates()
    {
        var tempDir = Directory.CreateTempSubdirectory().FullName;
        var spritesDir = Path.Combine(tempDir, "resources", "sprites");
        Directory.CreateDirectory(spritesDir);

        SaveSolidPng(Path.Combine(spritesDir, "packed.png"), 100, 100, SKColors.Red);
        File.WriteAllText(
            Path.Combine(spritesDir, "packed.json"),
            """
            {
              "texture": { "id": "test", "width": 100, "height": 100 },
              "sprites": {
                "icon": { "textureFrame": [90, 90, 20, 20], "pivot": [0.5, 0.5] }
              }
            }
            """);

        try
        {
            var validator = new ThemeValidator();
            var result = validator.Validate(tempDir);
            Assert.False(result.IsValid);
            Assert.Contains(result.Issues, i => i.IsError && i.Message.Contains("exceeds atlas dimensions"));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void AcceptsNesThemeWithoutPackedAtlas()
    {
        var tempDir = Directory.CreateTempSubdirectory().FullName;
        var spritesDir = Path.Combine(tempDir, "resources", "sprites");
        Directory.CreateDirectory(spritesDir);
        SaveSolidPng(Path.Combine(spritesDir, "nes.png"), 64, 64, SKColors.Red);
        File.WriteAllText(
            Path.Combine(spritesDir, "nes.json"),
            """{"texture":{"id":"nes","width":64,"height":64},"sprites":{"a":{"textureFrame":[0,0,8,8]},"b":{"textureFrame":[8,0,8,8]}}}""");

        try
        {
            var result = new ThemeValidator().Validate(tempDir);

            Assert.True(result.IsValid);
            Assert.DoesNotContain(result.Issues, issue => issue.Message.Contains("packed"));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ReportsThemeWithoutAnyAtlas()
    {
        var tempDir = Directory.CreateTempSubdirectory().FullName;

        try
        {
            var result = new ThemeValidator().Validate(tempDir);

            Assert.False(result.IsValid);
            Assert.Contains(result.Issues, issue => issue.IsError && issue.Message.StartsWith("No sprite atlas found", StringComparison.Ordinal));
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
