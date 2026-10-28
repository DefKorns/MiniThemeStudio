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
    public async Task AnImageTheThemeAddedAndNoSceneDrawsCanBeDeleted()
    {
        // an image left by a removed layer: registered, but drawn by nothing
        var draft = _app.Window.CurrentDraftPath!;
        TestApp.WriteFile(draft, "dependencies.json", """
            {"resources":[
              {"id":"scene","type":"Scene","dependencies":[{"id":"wall","refCount":1}],"path":"/prefab/bg.scn","groupId":"1"},
              {"id":"wall","type":"TextureResource","path":"/layout/asset/sprite/wallpaper/wall.png","groupId":"2"},
              {"id":"hills","type":"TextureResource","path":"/layout/asset/sprite/wallpaper/hills.png","groupId":"3"},
              {"id":"sheet_hills","type":"SpriteSheet","dependencies":[{"id":"hills","refCount":1}],"path":"/layout/asset/sprite/wallpaper/hills.json","groupId":"4"}
            ],"resourceGroups":[]}
            """);
        TestApp.WriteBytes(draft, "resources/layout/asset/sprite/wallpaper/hills.png", TestApp.Png);
        TestApp.WriteFile(draft, "resources/layout/asset/sprite/wallpaper/hills.json", """{"texture":{"id":"hills","width":1,"height":1},"sprites":{}}""");
        _app.Session.Reload();
        var wallpapers = _app.Window.WallpapersTab;
        wallpapers.SelectedWallpaper = wallpapers.Wallpapers.Single(wallpaper => wallpaper.Name == "hills");
        Assert.Equal("Delete image", wallpapers.DeleteWallpaperText);

        await wallpapers.DeleteWallpaperCommand.ExecuteAsync(null);

        Assert.Empty(_app.Errors.Failures.Select(failure => $"{failure.Failure}: {failure.Exception.Message}"));
        Assert.DoesNotContain(wallpapers.Wallpapers, wallpaper => wallpaper.Name == "hills");
        Assert.DoesNotContain("hills", File.ReadAllText(Path.Combine(draft, "dependencies.json")), StringComparison.Ordinal);
        Assert.Empty(_app.Window.ValidationTab.MissingTextures);
    }

    [Fact]
    public async Task AReplacedWallpaperCanGoBackToTheBaseImage()
    {
        var wallpapers = _app.Window.WallpapersTab;
        TestApp.WriteBytes(_app.Root, "new_wall.png", TestApp.Png);
        _app.Files.Paths.Enqueue(Path.Combine(_app.Root, "new_wall.png"));
        await wallpapers.ReplaceWallpaperCommand.ExecuteAsync(null);
        Assert.Equal("Use the Base image", wallpapers.DeleteWallpaperText);

        await wallpapers.DeleteWallpaperCommand.ExecuteAsync(null);

        Assert.False(File.Exists(Path.Combine(_app.Window.CurrentDraftPath!, "resources", "layout", "asset", "sprite", "wallpaper", "wall.png")));
        Assert.True(wallpapers.SelectedWallpaper?.IsFromBaseUi);
        Assert.False(wallpapers.DeleteWallpaperCommand.CanExecute(null));
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
