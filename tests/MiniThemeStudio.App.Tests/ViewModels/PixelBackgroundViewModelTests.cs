using Avalonia.Media;
using MiniThemeStudio.App.Tests.TestDoubles;
using MiniThemeStudio.App.ViewModels;
using MiniThemeStudio.Infrastructure.Tests.Themes;
using SkiaSharp;

namespace MiniThemeStudio.App.Tests.ViewModels;

// the Wallpapers tab of a UI that draws its background with a script, as the NES and Famicom ones do
public sealed class PixelBackgroundViewModelTests : IDisposable
{
    private readonly TestApp _app = new();

    public PixelBackgroundViewModelTests()
    {
        string basePath = _app.CreateBase();
        Directory.Delete(Path.Combine(basePath, "resources", "layout", "asset", "sprite", "wallpaper"), true);
        TestApp.WriteBytes(basePath, "resources/scripts/colors_nes.lua", PaletteFixtures.SampleBytes);
        SavePng(Path.Combine(basePath, "resources", "sprites", "wallpaper-dots.png"), 32, 32);
        SavePng(Path.Combine(basePath, "resources", "sprites", "wallpaper-transit.png"), 32, 128);
        _app.Window.OpenTheme(_app.CreateTheme(), basePath);
    }

    public void Dispose() => _app.Dispose();

    private PixelBackgroundViewModel Background => _app.Window.WallpapersTab.PixelBackground;

    [Fact]
    public void TheColoursAndPicturesShowInsteadOfTheWallpapers()
    {
        Assert.True(Background.HasPixelBackground);
        Assert.False(_app.Window.WallpapersTab.HasWallpaperImages);
        Assert.Equal(["Dot", "Transition shapes"], Background.Images.Select(image => image.Label));
        Assert.Equal("32 × 128", Background.Images[1].SizeText);

        // the sample palette has bg_base = {0.5, 0.25, 0.75, 1}
        Assert.Equal(["Background", "Mario's colour", "Luigi's colour"], Background.Colors.Select(row => row.Label));
        BackgroundColorRow color = Background.Colors[0];
        Assert.Equal("Background", color.Label);
        Assert.Equal(Color.FromRgb(128, 64, 191), color.Saved);
    }

    [Fact]
    public async Task ANewColourIsWrittenToThePaletteOnApply()
    {
        Background.Colors[0].Color = Color.FromRgb(10, 20, 30);
        Assert.True(Background.Colors[0].IsChanged);

        await Background.ApplyColorsCommand.ExecuteAsync(null);

        Assert.Empty(_app.Errors.Failures.Select(failure => $"{failure.Failure}: {failure.Exception.Message}"));
        Assert.True(File.Exists(Path.Combine(_app.Window.CurrentDraftPath!, "resources", "scripts", "colors_nes.lua")));
        Assert.Equal(Color.FromRgb(10, 20, 30), Background.Colors[0].Saved);
        Assert.Equal("Changed background.", _app.Window.Status);
    }

    [Fact]
    public async Task TheBaseColoursCanBeRestored()
    {
        Assert.False(Background.RestoreBaseColorsCommand.CanExecute(null));
        Background.Colors[0].Color = Color.FromRgb(10, 20, 30);
        await Background.ApplyColorsCommand.ExecuteAsync(null);
        Assert.True(Background.RestoreBaseColorsCommand.CanExecute(null));

        await Background.RestoreBaseColorsCommand.ExecuteAsync(null);

        Assert.Equal(Color.FromRgb(128, 64, 191), Background.Colors[0].Saved);
        Assert.False(Background.RestoreBaseColorsCommand.CanExecute(null));
        Assert.Equal("The background uses the colours of the Base again.", _app.Window.Status);
    }

    [Fact]
    public async Task APictureKeepsItsSize()
    {
        string wrong = Path.Combine(_app.Root, "dot.png");
        SavePng(wrong, 16, 16);
        _app.Files.Paths.Enqueue(wrong);

        await Background.ReplaceImageCommand.ExecuteAsync(Background.Images[0]);

        (Exception? exception, string _) = Assert.Single(_app.Errors.Failures);
        Assert.Equal("Dot must be 32 × 32 pixels, like the image it replaces; dot.png is 16 × 16.", exception.Message);

        string right = Path.Combine(_app.Root, "dot32.png");
        SavePng(right, 32, 32);
        _app.Files.Paths.Enqueue(right);
        await Background.ReplaceImageCommand.ExecuteAsync(Background.Images[0]);

        Assert.True(File.Exists(Path.Combine(_app.Window.CurrentDraftPath!, "resources", "sprites", "wallpaper-dots.png")));
        Assert.Equal("Replaced Dot.", _app.Window.Status);
    }

    private static void SavePng(string path, int width, int height)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.White);
        using SKData png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream file = File.Create(path);
        png.SaveTo(file);
    }
}
