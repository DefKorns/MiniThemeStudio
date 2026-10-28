using System.Text.Json.Nodes;
using MiniThemeStudio.Infrastructure.Themes;
using SkiaSharp;

namespace MiniThemeStudio.Infrastructure.Tests.Themes;

// a NES-like Base whose sys_wallpaper_fg0.lua cuts two Mario pictures and a block from characters_sbm.png
public sealed class CharacterSheetResizeTests : IDisposable
{
    private readonly string _themePath = Directory.CreateTempSubdirectory().FullName;
    private readonly string _basePath = Directory.CreateTempSubdirectory().FullName;
    private readonly CharacterSheetEditor _editor = new();

    public CharacterSheetResizeTests()
    {
        var sprites = Path.Combine(_basePath, "resources", "sprites");
        Directory.CreateDirectory(sprites);
        using (var sheet = new SKBitmap(new SKImageInfo(128, 64, SKColorType.Rgba8888, SKAlphaType.Unpremul)))
        {
            sheet.Erase(SKColors.Transparent);
            Fill(sheet, 0, 1, 16, 16, SKColors.Red);
            Fill(sheet, 18, 1, 16, 16, SKColors.Green);
            Fill(sheet, 0, 35, 16, 16, SKColors.Yellow);
            using var png = sheet.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(Path.Combine(sprites, "characters_sbm.png"), png.ToArray());
        }

        File.WriteAllText(Path.Combine(sprites, "characters_sbm.json"), """{"texture":{"id":"sbm","width":128,"height":64},"sprites":{"Default":{"textureFrame":[0,0,232,32]}}}""");
        Directory.CreateDirectory(Path.Combine(_basePath, "resources", "scripts"));
        File.WriteAllBytes(Path.Combine(_basePath, "resources", "scripts", "sys_wallpaper_fg0.lua"), CharacterPatternFixtures.SampleBytes);
    }

    public void Dispose()
    {
        Directory.Delete(_themePath, true);
        Directory.Delete(_basePath, true);
    }

    [Fact]
    public void TheFramesAreThePlacesTheScriptCuts()
    {
        var sheet = Assert.Single(_editor.GetSheets(_themePath, _basePath));

        Assert.True(sheet.CanResize);
        Assert.Equal(["Mario", "Block"], sheet.RowNames);
        Assert.Equal(
            [new(0, 0, 0, 1, 16, 16, 0, 1, 16, 16), new(0, 1, 18, 1, 16, 16, 18, 1, 16, 16), new(1, 0, 0, 35, 16, 16, 0, 35, 16, 16)],
            sheet.Frames);
    }

    [Fact]
    public void ALargerRowLaysTheSheetOutAgainAndRewritesTheScript()
    {
        var sheet = _editor.GetSheets(_themePath, _basePath)[0];

        _editor.ResizeRow(_themePath, _basePath, sheet, 0, 28, 36);

        // Mario's cells are 28 x 36 two pixels apart; the block moves down under them
        var resized = _editor.GetSheets(_themePath, _basePath)[0];
        Assert.False(resized.IsFromBaseUi);
        Assert.Equal(
            [new(0, 0, 0, 1, 28, 36, 0, 1, 28, 36), new(0, 1, 30, 1, 28, 36, 30, 1, 28, 36), new(1, 0, 0, 38, 16, 16, 0, 38, 16, 16)],
            resized.Frames);

        var script = LuaCharacterPatterns.Read(File.ReadAllBytes(Path.Combine(_themePath, "resources", "scripts", "sys_wallpaper_fg0.lua")));
        Assert.Equal((28, 36), (script.Characters[0].Width, script.Characters[0].Height));
        Assert.Equal([(0, 38)], script.Characters[1].Frames);

        // the old pictures keep their pixels, centred at the bottom of their new cells
        using (var pixels = SKBitmap.Decode(Path.Combine(_themePath, "resources", "sprites", "characters_sbm.png")))
        {
            Assert.Equal((128, 64), (pixels.Width, pixels.Height));
            Assert.Equal(SKColors.Red, pixels.GetPixel(6, 21));
            Assert.Equal(0, pixels.GetPixel(6, 20).Alpha);
            Assert.Equal(SKColors.Green, pixels.GetPixel(36, 36));
            Assert.Equal(SKColors.Yellow, pixels.GetPixel(0, 38));
        }

        var json = JsonNode.Parse(File.ReadAllText(Path.Combine(_themePath, "resources", "sprites", "characters_sbm.json")))!;
        Assert.Equal(128, json["texture"]!["width"]!.GetValue<int>());

        // and a 27 x 36 picture now fits without being made smaller
        var picture = Path.Combine(_themePath, "big.png");
        using (var big = new SKBitmap(new SKImageInfo(27, 36, SKColorType.Rgba8888, SKAlphaType.Unpremul)))
        {
            big.Erase(SKColors.Blue);
            big.SetPixel(1, 1, SKColors.White);
            using var png = big.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(picture, png.ToArray());
        }

        Assert.Equal(1, _editor.ReplaceFrame(_themePath, _basePath, resized, resized.Frames[0], picture));
    }

    [Fact]
    public void AWiderSheetGrowsToTheNextPowerOfTwo()
    {
        var sheet = _editor.GetSheets(_themePath, _basePath)[0];

        _editor.ResizeRow(_themePath, _basePath, sheet, 0, 80, 40);

        using var pixels = SKBitmap.Decode(Path.Combine(_themePath, "resources", "sprites", "characters_sbm.png"));
        Assert.Equal((256, 64), (pixels.Width, pixels.Height));
        var json = JsonNode.Parse(File.ReadAllText(Path.Combine(_themePath, "resources", "sprites", "characters_sbm.json")))!;
        Assert.Equal(256, json["texture"]!["width"]!.GetValue<int>());
    }

    private static void Fill(SKBitmap bitmap, int x, int y, int width, int height, SKColor color)
    {
        for (var row = y; row < y + height; row++)
        {
            for (var column = x; column < x + width; column++)
            {
                bitmap.SetPixel(column, row, color);
            }
        }
    }
}
