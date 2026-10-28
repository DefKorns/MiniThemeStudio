using MiniThemeStudio.App.Tests.TestDoubles;

namespace MiniThemeStudio.App.Tests.ViewModels;

public sealed class WallpapersViewModelTests : IDisposable
{
    private readonly TestApp _app = new();

    public WallpapersViewModelTests()
    {
        var basePath = _app.CreateBase();
        _app.Window.OpenTheme(_app.CreateTheme(), basePath);
    }

    public void Dispose() => _app.Dispose();

    [Fact]
    public void ListsTheCoreWallpapersAndSelectsTheFirst()
    {
        var wallpapers = _app.Window.WallpapersTab;

        // the core layers are listed even when neither the theme nor the Base has them
        Assert.True(wallpapers.Wallpapers.Count > 1);
        var wall = wallpapers.Wallpapers[0];
        Assert.Equal("wall", wall.Name);
        Assert.Same(wall, wallpapers.SelectedWallpaper);
        Assert.NotEmpty(wall.UsedBy);
    }

    [Fact]
    public async Task ReplacingWritesTheImageToTheDraftAndRefreshesThePreview()
    {
        var wallpapers = _app.Window.WallpapersTab;
        TestApp.WriteBytes(_app.Root, "new_wall.png", TestApp.Png);
        _app.Files.Paths.Enqueue(Path.Combine(_app.Root, "new_wall.png"));
        var revision = wallpapers.PreviewRevision;

        await wallpapers.ReplaceWallpaperCommand.ExecuteAsync(null);

        Assert.Empty(_app.Errors.Failures.Select(failure => $"{failure.Failure}: {failure.Exception.Message}"));
        Assert.True(File.Exists(Path.Combine(_app.Window.CurrentDraftPath!, "resources", "layout", "asset", "sprite", "wallpaper", "wall.png")));
        Assert.Equal(revision + 1, wallpapers.PreviewRevision);
        Assert.Equal("wall", wallpapers.SelectedWallpaper?.Name);
        Assert.True(_app.Window.HasUnsavedChanges);
    }

    [Fact]
    public async Task CancellingTheFileDialogChangesNothing()
    {
        var wallpapers = _app.Window.WallpapersTab;

        await wallpapers.ReplaceWallpaperCommand.ExecuteAsync(null);

        Assert.False(_app.Window.HasUnsavedChanges);
        Assert.False(Directory.Exists(Path.Combine(_app.Window.CurrentDraftPath!, "resources", "layout")));
    }

    [Fact]
    public async Task ExportingSavesThePngWhereTheUserChose()
    {
        var wallpapers = _app.Window.WallpapersTab;
        var target = Path.Combine(_app.Root, "export", "wall.png");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        _app.Files.Paths.Enqueue(target);

        await wallpapers.ExportWallpaperCommand.ExecuteAsync(null);

        Assert.True(File.Exists(target));
        Assert.Equal("Exported wallpaper Main Wallpaper (wall).", _app.Window.Status);
    }
}
