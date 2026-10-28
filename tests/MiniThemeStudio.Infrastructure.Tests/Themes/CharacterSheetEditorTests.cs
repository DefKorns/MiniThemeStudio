using MiniThemeStudio.Core.Themes;
using MiniThemeStudio.Infrastructure.Themes;
using SkiaSharp;

namespace MiniThemeStudio.Infrastructure.Tests.Themes;

public sealed class CharacterSheetEditorTests : IDisposable
{
    private readonly string _themePath = Directory.CreateTempSubdirectory().FullName;
    private readonly string _basePath = Directory.CreateTempSubdirectory().FullName;
    private readonly CharacterSheetEditor _editor = new();

    // two rows: a 12 x 16 and a 15 x 16 picture, then one 16 x 16 picture
    public CharacterSheetEditorTests()
    {
        SavePng(Path.Combine(_basePath, "resources", "sprites", "characters_sbm.png"), 128, 64, (2, 1, 12, 16), (18, 1, 15, 16), (0, 35, 16, 16));
    }

    public void Dispose()
    {
        Directory.Delete(_themePath, true);
        Directory.Delete(_basePath, true);
    }

    [Fact]
    public void FindsThePicturesBetweenTransparentLines()
    {
        CharacterSheet sheet = Assert.Single(_editor.GetSheets(_themePath, _basePath));

        Assert.Equal("characters_sbm", sheet.Name);
        Assert.True(sheet.IsFromBaseUi);
        Assert.Equal((128, 64), (sheet.Width, sheet.Height));
        Assert.Equal(
            [new(0, 0, 2, 1, 12, 16, 0, 1, 16, 16), new(0, 1, 18, 1, 15, 16, 18, 1, 16, 16), new(1, 0, 0, 35, 16, 16, 0, 35, 16, 16)],
            sheet.Frames);
    }

    [Fact]
    public void TheSmallCharactersOfTheBackgroundAreASecondSheet()
    {
        SavePng(Path.Combine(_basePath, "resources", "sprites", "wallpaper-hvc.png"), 64, 16, (2, 0, 12, 16), (32, 0, 32, 16));

        IReadOnlyList<CharacterSheet> sheets = _editor.GetSheets(_themePath, _basePath);

        Assert.Equal(["characters_sbm", "wallpaper-hvc"], sheets.Select(sheet => sheet.Name));
        Assert.Equal([new(0, 0, 2, 0, 12, 16, 0, 0, 16, 16), new(0, 1, 32, 0, 32, 16, 32, 0, 32, 16)], sheets[1].Frames);
    }

    [Fact]
    public void AUiWithoutASheetHasNone()
    {
        File.Delete(Path.Combine(_basePath, "resources", "sprites", "characters_sbm.png"));

        Assert.Empty(_editor.GetSheets(_themePath, _basePath));
    }

    [Fact]
    public void APictureStandsWhereTheOldOneStoodAndTheFramesStayPut()
    {
        CharacterSheet sheet = _editor.GetSheets(_themePath, _basePath)[0];
        CharacterSheetFrame frame = sheet.Frames[1];
        string picture = Path.Combine(_themePath, "luigi.png");

        // a 4 x 4 picture with transparent edges: centred on the old 15 x 16 one and standing on its bottom line
        SavePng(picture, 15, 16, (4, 4, 4, 4));
        _editor.ReplaceFrame(_themePath, _basePath, sheet, frame, picture);

        string draftSheet = Path.Combine(_themePath, "resources", "sprites", "characters_sbm.png");
        using (var pixels = SKBitmap.Decode(draftSheet))
        {
            Assert.Equal(0, pixels.GetPixel(18, 1).Alpha);
            Assert.Equal(255, pixels.GetPixel(23, 13).Alpha);
            Assert.Equal(255, pixels.GetPixel(26, 16).Alpha);
            Assert.Equal(0, pixels.GetPixel(27, 16).Alpha);
            Assert.Equal(255, pixels.GetPixel(2, 1).Alpha);
        }

        CharacterSheet again = _editor.GetSheets(_themePath, _basePath)[0];
        Assert.False(again.IsFromBaseUi);
        Assert.Equal(frame, again.Frames[1]);
    }

    [Fact]
    public void PicturesAndSheetsKeepTheirSize()
    {
        CharacterSheet sheet = _editor.GetSheets(_themePath, _basePath)[0];
        CharacterSheetFrame frame = sheet.Frames[0];
        string wrong = Path.Combine(_themePath, "wrong.png");
        SavePng(wrong, 17, 16);

        Assert.Throws<InvalidDataException>(() => _editor.ReplaceSheet(_themePath, _basePath, sheet, wrong));

        string whole = Path.Combine(_themePath, "sheet.png");
        SavePng(whole, 128, 64, (0, 0, 8, 8));
        _editor.ReplaceSheet(_themePath, _basePath, sheet, whole);
        Assert.True(File.Exists(Path.Combine(_themePath, "resources", "sprites", "characters_sbm.png")));
    }

    [Fact]
    public void EnlargedPixelArtIsDrawnAtItsOwnPixelSize()
    {
        CharacterSheet sheet = _editor.GetSheets(_themePath, _basePath)[0];

        // a 16 x 16 sprite drawn three times larger, with a 3 x 3 dot in one corner
        string big = Path.Combine(_themePath, "big.png");
        SavePng(big, 48, 48, (0, 0, 48, 48));
        using (SKBitmap bitmap = SKBitmap.Decode(big).Copy(SKColorType.Rgba8888))
        {
            for (int y = 0; y < 3; y++)
            {
                for (int x = 0; x < 3; x++)
                {
                    bitmap.SetPixel(x, y, SKColors.Blue);
                }
            }

            using SKData png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(big, png.ToArray());
        }

        Assert.Equal(1 / 3.0, _editor.ReplaceFrame(_themePath, _basePath, sheet, sheet.Frames[2], big), 6);

        using var pixels = SKBitmap.Decode(Path.Combine(_themePath, "resources", "sprites", "characters_sbm.png"));
        Assert.Equal(SKColors.Blue, pixels.GetPixel(0, 35));
        Assert.Equal(SKColors.Red, pixels.GetPixel(1, 35));
    }

    [Fact]
    public void ALargePictureIsMadeSmallerToFit()
    {
        CharacterSheet sheet = _editor.GetSheets(_themePath, _basePath)[0];
        // a sprite drawn at its own pixel size, with one pixel of another colour
        string large = Path.Combine(_themePath, "large.png");
        SavePng(large, 27, 36);
        using (SKBitmap bitmap = SKBitmap.Decode(large).Copy(SKColorType.Rgba8888))
        {
            bitmap.SetPixel(1, 1, SKColors.Blue);
            using SKData png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(large, png.ToArray());
        }

        double scale = _editor.ReplaceFrame(_themePath, _basePath, sheet, sheet.Frames[0], large);

        Assert.Equal(16 / 36.0, scale, 6);
        using var pixels = SKBitmap.Decode(Path.Combine(_themePath, "resources", "sprites", "characters_sbm.png"));

        // 12 x 16, standing on the bottom line of the old picture and centred on it
        Assert.Equal(255, pixels.GetPixel(2, 1).Alpha);
        Assert.Equal(255, pixels.GetPixel(13, 16).Alpha);
        Assert.Equal(0, pixels.GetPixel(14, 16).Alpha);
    }

    private static void SavePng(string path, int width, int height, params (int X, int Y, int Width, int Height)[] inked)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        bitmap.Erase(SKColors.Transparent);
        foreach ((int x, int y, int w, int h) in inked.Length == 0 ? [(0, 0, width, height)] : inked)
        {
            for (int row = y; row < y + h; row++)
            {
                for (int column = x; column < x + w; column++)
                {
                    bitmap.SetPixel(column, row, SKColors.Red);
                }
            }
        }

        using SKData png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream file = File.Create(path);
        png.SaveTo(file);
    }
}
