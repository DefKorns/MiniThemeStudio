using MiniThemeStudio.Core.Firmware;
using MiniThemeStudio.Core.Themes;
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
        InstalledBaseInfo left = CreateBase("left", 16, 16, "[0,0,8,16]");
        InstalledBaseInfo right = CreateBase("right", 16, 16, "[8,0,8,16]");
        InstalledBaseInfo other = CreateBase("other", 32, 32, "[0,0,8,8]");
        string imagePath = Path.Combine(_rootPath, "packed.png");
        using (var bitmap = new SKBitmap(16, 16))
        {
            bitmap.Erase(SKColors.Transparent);
            for (int y = 0; y < 16; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    bitmap.SetPixel(x, y, SKColors.Red);
                }
            }

            SavePng(bitmap, imagePath);
        }

        IReadOnlyList<AtlasLayoutMatch> matches = new AtlasLayoutMatcher().Rank(imagePath, [right, other, left]);

        Assert.Equal(["left", "right"], matches.Select(match => match.Base.Definition.Id));
        Assert.Equal(100, matches[0].CoveragePercent);
        Assert.Equal(1, matches[0].FramesWithContent);
        Assert.Equal(0, matches[1].CoveragePercent);
    }

    private InstalledBaseInfo CreateBase(string id, int width, int height, string frame)
    {
        string basePath = Path.Combine(_rootPath, id);
        string spritesPath = Path.Combine(basePath, "resources", "sprites");
        Directory.CreateDirectory(spritesPath);
        File.WriteAllText(
            Path.Combine(spritesPath, "packed.json"),
            $$"""{"texture":{"id":"{{id}}","width":{{width}},"height":{{height}}},"sprites":{"s":{"textureFrame":""" + frame + "}}}");
        FirmwareDefinition definition = FirmwareRegistryTests.CreateDefinition(id, new string('a', 64), new string('b', 64));
        return new InstalledBaseInfo(definition, basePath, BaseInstallStatus.Valid);
    }

    internal static void SavePng(SKBitmap bitmap, string path)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream stream = File.Create(path);
        data.SaveTo(stream);
    }
}
