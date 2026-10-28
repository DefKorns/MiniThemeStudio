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
        var sheet = _editor.GetSheet(_themePath, _basePath)!;

        Assert.True(sheet.IsFromBaseUi);
        Assert.Equal((128, 64), (sheet.Width, sheet.Height));
        Assert.Equal(
            [new(0, 0, 2, 1, 12, 16), new(0, 1, 18, 1, 15, 16), new(1, 0, 0, 35, 16, 16)],
            sheet.Frames);
    }

    [Fact]
    public void AUiWithoutASheetHasNone()
    {
        File.Delete(Path.Combine(_basePath, "resources", "sprites", "characters_sbm.png"));

        Assert.Null(_editor.GetSheet(_themePath, _basePath));
    }

    [Fact]
    public void AFrameIsDrawnInPlaceAndKeepsItsPlaceForTheNextChange()
    {
        var frame = _editor.GetSheet(_themePath, _basePath)!.Frames[1];
        var picture = Path.Combine(_themePath, "luigi.png");

        // a picture with transparent edges, which would make the frame smaller if it were found again
        SavePng(picture, 15, 16, (4, 4, 4, 4));
        _editor.ReplaceFrame(_themePath, _basePath, frame, picture);

        var draftSheet = Path.Combine(_themePath, "resources", "sprites", "characters_sbm.png");
        using (var sheet = SKBitmap.Decode(draftSheet))
        {
            Assert.Equal(0, sheet.GetPixel(18, 1).Alpha);
            Assert.Equal(255, sheet.GetPixel(22, 5).Alpha);
            Assert.Equal(255, sheet.GetPixel(2, 1).Alpha);
        }

        var again = _editor.GetSheet(_themePath, _basePath)!;
        Assert.False(again.IsFromBaseUi);
        Assert.Equal(frame, again.Frames[1]);
    }

    [Fact]
    public void PicturesAndSheetsKeepTheirSize()
    {
        var frame = _editor.GetSheet(_themePath, _basePath)!.Frames[0];
        var wrong = Path.Combine(_themePath, "wrong.png");
        SavePng(wrong, 16, 16);

        var picture = Assert.Throws<InvalidDataException>(() => _editor.ReplaceFrame(_themePath, _basePath, frame, wrong));
        Assert.Equal("The picture must be 12 × 16 pixels, like the one it replaces; wrong.png is 16 × 16.", picture.Message);
        Assert.Throws<InvalidDataException>(() => _editor.ReplaceSheet(_themePath, _basePath, wrong));

        var sheet = Path.Combine(_themePath, "sheet.png");
        SavePng(sheet, 128, 64, (0, 0, 8, 8));
        _editor.ReplaceSheet(_themePath, _basePath, sheet);
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
