using MiniThemeStudio.Core.Firmware;
using MiniThemeStudio.Infrastructure.Tests.Firmware;
using MiniThemeStudio.Infrastructure.Themes;
using SkiaSharp;

namespace MiniThemeStudio.Infrastructure.Tests.Themes;

public sealed class AtlasLayoutMatcherTests : IDisposable
{
    private readonly string _rootPath = Directory.CreateTempSubdirectory().FullName;

    public void Dispose()
    {
        Directory.Delete(_rootPath, true);
    }

    [Fact]
    public void RanksBaseWhoseFramesHoldTheVisiblePixelsFirst()
    {
        var left = CreateBase("left", 16, 16, "[0,0,8,16]");
        var right = CreateBase("right", 16, 16, "[8,0,8,16]");
        var other = CreateBase("other", 32, 32, "[0,0,8,8]");
        var imagePath = Path.Combine(_rootPath, "packed.png");
        using (var bitmap = new SKBitmap(16, 16))
        {
            bitmap.Erase(SKColors.Transparent);
            for (var y = 0; y < 16; y++)
            {
                for (var x = 0; x < 8; x++)
                {
                    bitmap.SetPixel(x, y, SKColors.Red);
                }
            }

            SavePng(bitmap, imagePath);
        }

        var matches = new AtlasLayoutMatcher().Rank(imagePath, [right, other, left]);

        Assert.Equal(["left", "right"], matches.Select(match => match.Base.Definition.Id));
        Assert.Equal(100, matches[0].CoveragePercent);
        Assert.Equal(1, matches[0].FramesWithContent);
        Assert.Equal(0, matches[1].CoveragePercent);
    }

    private InstalledBaseInfo CreateBase(string id, int width, int height, string frame)
    {
        var basePath = Path.Combine(_rootPath, id);
        var spritesPath = Path.Combine(basePath, "resources", "sprites");
        Directory.CreateDirectory(spritesPath);
        File.WriteAllText(
            Path.Combine(spritesPath, "packed.json"),
            $$"""{"texture":{"id":"{{id}}","width":{{width}},"height":{{height}}},"sprites":{"s":{"textureFrame":""" + frame + "}}}");
        var definition = FirmwareRegistryTests.CreateDefinition(id, new string('a', 64), new string('b', 64));
        return new InstalledBaseInfo(definition, basePath, BaseInstallStatus.Valid);
    }

    internal static void SavePng(SKBitmap bitmap, string path)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.SaveTo(stream);
    }
}
