using MiniThemeStudio.App.Tests.TestDoubles;
using SkiaSharp;

namespace MiniThemeStudio.App.Tests.ViewModels;

// the Characters tab of a UI that draws its characters from one sheet, as the NES and Famicom ones do
public sealed class CharacterSheetTests : IDisposable
{
    private readonly TestApp _app = new();

    public CharacterSheetTests()
    {
        var basePath = _app.CreateBase();
        SavePng(Path.Combine(basePath, "resources", "sprites", "characters_sbm.png"), 128, 64, (2, 1, 12, 16), (18, 1, 15, 16), (2, 18, 12, 16));
        _app.Window.OpenTheme(_app.CreateTheme(), basePath);
    }

    public void Dispose() => _app.Dispose();

    [Fact]
    public void TheSheetShowsInsteadOfTheAnimations()
    {
        var characters = _app.Window.CharactersTab;

        Assert.True(characters.ShowSheet);
        Assert.False(characters.ShowSimpleFrames);
        Assert.False(characters.ShowAdvancedFrames);
        Assert.Equal(["Mario", "Luigi"], characters.SheetRows.Select(row => row.Label));
        Assert.Equal(["Mario 1", "Mario 2"], characters.SheetRows[0].Frames.Select(frame => frame.Label));
        Assert.Equal("12 × 16", characters.SheetRows[1].Frames[0].SizeText);
        Assert.NotNull(characters.SheetImage);
    }

    [Fact]
    public async Task APictureReplacesItsFrameInTheThemesSheet()
    {
        var characters = _app.Window.CharactersTab;
        var picture = Path.Combine(_app.Root, "luigi.png");
        SavePng(picture, 12, 16);
        _app.Files.Paths.Enqueue(picture);

        await characters.ReplaceSheetFrameCommand.ExecuteAsync(characters.SheetRows[1].Frames[0]);

        Assert.Empty(_app.Errors.Failures.Select(failure => $"{failure.Failure}: {failure.Exception.Message}"));
        Assert.True(File.Exists(Path.Combine(_app.Window.CurrentDraftPath!, "resources", "sprites", "characters_sbm.png")));
        Assert.Equal("Replaced Luigi 1.", _app.Window.Status);
        Assert.False(characters.Sheet!.IsFromBaseUi);
        Assert.True(_app.Window.HasUnsavedChanges);
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
                    bitmap.SetPixel(column, row, SKColors.Green);
                }
            }
        }

        using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        using var file = File.Create(path);
        png.SaveTo(file);
    }
}
