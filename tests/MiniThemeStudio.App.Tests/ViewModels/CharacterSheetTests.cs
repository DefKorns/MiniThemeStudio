using MiniThemeStudio.App.Models;
using MiniThemeStudio.App.Tests.TestDoubles;
using MiniThemeStudio.Core.Themes;
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
        SavePng(Path.Combine(basePath, "resources", "sprites", "wallpaper-hvc.png"), 64, 16, (2, 0, 12, 16), (18, 0, 12, 16), (32, 0, 32, 16));
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
        Assert.Equal(["Menu characters", "Background characters"], characters.Sheets.Select(sheet => sheet.Title));
        var menu = characters.Sheets[0];
        Assert.Equal(["Mario", "Luigi"], menu.Rows.Select(row => row.Label));
        Assert.Equal(["Mario 1", "Mario 2"], menu.Rows[0].Frames.Select(frame => frame.Label));
        Assert.Equal("up to 16 × 16", menu.Rows[1].Frames[0].SizeText);
        Assert.NotNull(menu.Image);
        Assert.Equal(["Mario", "Luigi", "Mario and Luigi"], characters.Sheets[1].Rows[0].Frames.Select(frame => frame.Label));
        Assert.Equal(["Mario", "Luigi", "Small Mario and Luigi"], characters.SheetGroups.Select(group => group.Label));
        Assert.Equal("Mario", characters.SelectedSheetGroup?.Label);
        Assert.Equal("Menu characters", characters.SelectedSheetGroup?.Sheet.Title);

        // the small characters are edited here, not with the background
        Assert.DoesNotContain(_app.Window.WallpapersTab.PixelBackground.Images, image => image.Name == "wallpaper-hvc");
    }

    [Fact]
    public async Task APictureReplacesItsFrameInTheThemesSheet()
    {
        var characters = _app.Window.CharactersTab;
        var picture = Path.Combine(_app.Root, "luigi.png");
        SavePng(picture, 12, 16);
        _app.Files.Paths.Enqueue(picture);

        await characters.ReplaceSheetFrameCommand.ExecuteAsync(characters.Sheets[0].Rows[1].Frames[0]);

        Assert.Empty(_app.Errors.Failures.Select(failure => $"{failure.Failure}: {failure.Exception.Message}"));
        Assert.True(File.Exists(Path.Combine(_app.Window.CurrentDraftPath!, "resources", "sprites", "characters_sbm.png")));
        Assert.Equal("Replaced Luigi 1.", _app.Window.Status);
        Assert.False(characters.Sheets[0].Sheet.IsFromBaseUi);
        Assert.True(_app.Window.HasUnsavedChanges);
    }

    [Fact]
    public async Task TheFramesOfARowArePickedOnASpriteSheet()
    {
        var characters = _app.Window.CharactersTab;
        var picked = Enumerable.Range(0, 3).Select(index =>
        {
            var path = Path.Combine(_app.Root, $"picked{index}.png");
            SavePng(path, 16, 16, (2, 2, 10, 14));
            return path;
        }).ToList();
        _app.AppDialogs.SpriteSheet = new SpriteSheetResult(picked, new AtlasSize(16, 16));

        await characters.ImportRowCommand.ExecuteAsync(characters.Sheets[0].Rows[0]);

        Assert.Empty(_app.Errors.Failures.Select(failure => $"{failure.Failure}: {failure.Exception.Message}"));
        Assert.Equal("Mario: replaced 2 of 2 pictures from the sprite sheet.", _app.Window.Status);
        Assert.False(characters.Sheets[0].Sheet.IsFromBaseUi);
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
