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
        var sheet = Assert.Single(_editor.GetSheets(_themePath, _basePath));

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

        var sheets = _editor.GetSheets(_themePath, _basePath);

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
        var sheet = _editor.GetSheets(_themePath, _basePath)[0];
        var frame = sheet.Frames[1];
        var picture = Path.Combine(_themePath, "luigi.png");

        // a 4 x 4 picture with transparent edges: centred on the old 15 x 16 one and standing on its bottom line
        SavePng(picture, 15, 16, (4, 4, 4, 4));
        _editor.ReplaceFrame(_themePath, _basePath, sheet, frame, picture);

        var draftSheet = Path.Combine(_themePath, "resources", "sprites", "characters_sbm.png");
        using (var pixels = SKBitmap.Decode(draftSheet))
        {
            Assert.Equal(0, pixels.GetPixel(18, 1).Alpha);
            Assert.Equal(255, pixels.GetPixel(23, 13).Alpha);
            Assert.Equal(255, pixels.GetPixel(26, 16).Alpha);
            Assert.Equal(0, pixels.GetPixel(27, 16).Alpha);
            Assert.Equal(255, pixels.GetPixel(2, 1).Alpha);
        }

        var again = _editor.GetSheets(_themePath, _basePath)[0];
        Assert.False(again.IsFromBaseUi);
        Assert.Equal(frame, again.Frames[1]);
    }

    [Fact]
    public void PicturesAndSheetsKeepTheirSize()
    {
        var sheet = _editor.GetSheets(_themePath, _basePath)[0];
        var frame = sheet.Frames[0];
        var wrong = Path.Combine(_themePath, "wrong.png");
        SavePng(wrong, 17, 16);

        var picture = Assert.Throws<InvalidDataException>(() => _editor.ReplaceFrame(_themePath, _basePath, sheet, frame, wrong));
        Assert.Equal("The picture can be 16 × 16 pixels at most, the room the frame has; wrong.png is 17 × 16 without its transparent edges.", picture.Message);
        Assert.Throws<InvalidDataException>(() => _editor.ReplaceSheet(_themePath, _basePath, sheet, wrong));

        var whole = Path.Combine(_themePath, "sheet.png");
        SavePng(whole, 128, 64, (0, 0, 8, 8));
        _editor.ReplaceSheet(_themePath, _basePath, sheet, whole);
        Assert.True(File.Exists(Path.Combine(_themePath, "resources", "sprites", "characters_sbm.png")));
    }

    private static void SavePng(string path, int width, int height, params (int X, int Y, int Width, int Height)[] inked)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        bitmap.Erase(SKColors.Transparent);
        foreach (var (x, y, w, h) in inked.Length == 0 ? [(0, 0, width, height)] : inked)
        {
            for (var row = y; row < y + h; row++)
            {
                for (var column = x; column < x + w; column++)
                {
                    bitmap.SetPixel(column, row, SKColors.Red);
                }
            }
        }

        using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        using var file = File.Create(path);
        png.SaveTo(file);
    }
}
