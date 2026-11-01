using MiniThemeStudio.Core.Themes;
using MiniThemeStudio.Infrastructure.Themes;
using SkiaSharp;

namespace MiniThemeStudio.Infrastructure.Tests.Themes;

public sealed class TextureAtlasEditorTests
{
    [Fact]
    public void ReplaceSpriteChangesOnlySelectedFrame()
    {
        string rootPath = Directory.CreateTempSubdirectory().FullName;
        string atlasPath = Path.Combine(rootPath, "packed.png");
        string replacementPath = Path.Combine(rootPath, "replacement.png");
        SaveSolidPng(atlasPath, 4, 4, SKColors.Red);
        SaveSolidPng(replacementPath, 2, 2, SKColors.Blue);
        SpriteRegion sprite = CreateSprite(1, 1, 2, 2);

        try
        {
            new TextureAtlasEditor().ReplaceSprite(
                new SpriteReplacementRequest(atlasPath, sprite, replacementPath));

            using var atlas = SKBitmap.Decode(atlasPath);
            Assert.Equal(SKColors.Red, atlas.GetPixel(0, 0));
            Assert.Equal(SKColors.Blue, atlas.GetPixel(1, 1));
            Assert.Equal(SKColors.Blue, atlas.GetPixel(2, 2));
            Assert.Equal(SKColors.Red, atlas.GetPixel(3, 3));
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    [Fact]
    public void ReplaceSpriteRejectsIncorrectDimensionsWithoutBackup()
    {
        string rootPath = Directory.CreateTempSubdirectory().FullName;
        string atlasPath = Path.Combine(rootPath, "packed.png");
        string replacementPath = Path.Combine(rootPath, "replacement.png");
        SaveSolidPng(atlasPath, 4, 4, SKColors.Red);
        SaveSolidPng(replacementPath, 3, 2, SKColors.Blue);
        SpriteRegion sprite = CreateSprite(1, 1, 2, 2);

        try
        {
            Func<SpriteReplacementResult> action = () => new TextureAtlasEditor().ReplaceSprite(
                new SpriteReplacementRequest(atlasPath, sprite, replacementPath));

            InvalidDataException exception = Assert.Throws<InvalidDataException>(action);
            Assert.Contains("must be 2 x 2 pixels", exception.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(atlasPath + ".bak"));
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    private static SpriteRegion CreateSprite(int x, int y, int width, int height)
    {
        return new SpriteRegion(
            "sprite.png",
            new AtlasRectangle(x, y, width, height),
            new AtlasPoint(width / 2.0, height / 2.0),
            new AtlasSize(width, height),
            new AtlasPoint(0, 0));
    }

    private static void SaveSolidPng(
        string path,
        int width,
        int height,
        SKColor color)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(color);
        using var image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream stream = File.Create(path);
        data.SaveTo(stream);
    }
}
