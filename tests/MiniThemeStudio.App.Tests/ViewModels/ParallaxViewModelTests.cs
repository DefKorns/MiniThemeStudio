using MiniThemeStudio.App.Tests.TestDoubles;
using MiniThemeStudio.App.ViewModels;

namespace MiniThemeStudio.App.Tests.ViewModels;

public sealed class ParallaxViewModelTests : IDisposable
{
    private readonly TestApp _app = new();
    private readonly string _basePath;

    public ParallaxViewModelTests()
    {
        _basePath = _app.CreateBase();
    }

    public void Dispose() => _app.Dispose();

    [Fact]
    public void ABaseWithoutAScrollingBackgroundSaysSo()
    {
        _app.Window.OpenTheme(_app.CreateTheme(), _basePath);

        var parallax = _app.Window.WallpapersTab.Parallax;

        Assert.False(parallax.HasBackgrounds);
        Assert.False(parallax.AddLayerCommand.CanExecute(null));
    }

    [Fact]
    public void ShowsTheLayersOfTheScrollingBackground()
    {
        var parallax = OpenScrollingTheme();

        Assert.Equal("demo_bg/RootPane (lyt_wallpaper)", parallax.SelectedBackground?.DisplayName);
        Assert.Equal(["wall_far"], parallax.Layers.Select(row => row.Name));
        Assert.Equal("layer -1 · 180 px/s", parallax.SelectedLayer?.Detail);
        Assert.Equal(1, parallax.LayerLoops);
        Assert.StartsWith("Every layer slides a whole number of times its own width in each 8.53 s cycle", parallax.CycleText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ANewLayerIsAddedFromAPngAndSelected()
    {
        var parallax = OpenScrollingTheme();
        TestApp.WriteBytes(_app.Root, "hills.png", TestApp.Png);
        _app.Files.Paths.Enqueue(Path.Combine(_app.Root, "hills.png"));
        parallax.NewLayerName = "hills";
        parallax.NewLayerZIndex = -2;
        parallax.NewLayerLoops = 2;

        await parallax.AddLayerCommand.ExecuteAsync(null);

        Assert.Empty(_app.Errors.Failures.Select(failure => $"{failure.Failure}: {failure.Exception.Message}"));
        Assert.Equal(["hills", "wall_far"], parallax.Layers.Select(row => row.Name));
        Assert.Equal("hills", parallax.SelectedLayer?.Name);
        Assert.Equal(2, parallax.LayerLoops);
        Assert.Equal("Added the scrolling layer hills to demo_bg/RootPane.", _app.Window.Status);

        // the new image is also one of the wallpapers
        Assert.Contains(_app.Window.WallpapersTab.Wallpapers, wallpaper => wallpaper.Name == "hills");
    }

    [Fact]
    public async Task TheSpeedOfTheSelectedLayerCanChange()
    {
        var parallax = OpenScrollingTheme();
        parallax.LayerLoops = 2;

        await parallax.ApplySpeedCommand.ExecuteAsync(null);

        Assert.Equal("wall_far now scrolls at 360 px/s.", _app.Window.Status);
        Assert.Equal("wall_far", parallax.SelectedLayer?.Name);
        Assert.True(_app.Window.HasUnsavedChanges);
    }

    [Fact]
    public async Task RemovingALayerAsksFirst()
    {
        var parallax = OpenScrollingTheme();
        _app.Dialogs.ConfirmationAnswer = false;

        await parallax.RemoveLayerCommand.ExecuteAsync(null);
        Assert.Single(parallax.Layers);

        _app.Dialogs.ConfirmationAnswer = true;
        await parallax.RemoveLayerCommand.ExecuteAsync(null);

        Assert.Empty(_app.Errors.Failures.Select(failure => $"{failure.Failure}: {failure.Exception.Message}"));
        Assert.Empty(parallax.Layers);
        Assert.Equal("Removed the scrolling layer wall_far.", _app.Window.Status);
    }

    // a Base whose demo background slides one wall over an 8.53 s cycle, as the SNES ones do
    private ParallaxViewModel OpenScrollingTheme()
    {
        TestApp.WriteFile(_basePath, "dependencies.json", """
            {"resources":[
              {"id":"scene","type":"Scene","dependencies":[{"id":"wall","refCount":1}],"path":"/prefab/bg.scn","groupId":"1"},
              {"id":"anim","type":"SceneAnimation","path":"/layout/lyt_wallpaper.sceneanimation","groupId":"2"},
              {"id":"wall","type":"TextureResource","path":"/layout/asset/sprite/wallpaper/wall.png","groupId":"3"}
            ],"resourceGroups":[]}
            """);
        TestApp.WriteFile(_basePath, "resources/prefab/bg.scn", """
            {"rootWorldNode":{"id":"r","name":"BG","components":[],"children":[
              {"id":"demo","name":"demo_bg","components":[],"children":[
                {"id":"pane","name":"RootPane","components":[{"type":"AnimatorComponent"}],"children":[
                  {"id":"f","name":"wall_far","zIndex":-1,"components":[{"type":"TextureComponent","textureId":"wall","size":[1536,543],"uvRepeat":[1,1]}],"children":[]}
                ]}
              ]}
            ]}}
            """);
        TestApp.WriteFile(_basePath, "resources/layout/lyt_wallpaper.sceneanimation", """
            {"duration":8.533333333333333,"animations":[
              {"worldNodePath":["wall_far"],"type":"float","componentType":"TextureComponent","propertyId":"TextureOffsetX","keyCurves":[{"time":0,"interp":"hermite","value":[0,1,0.1171875,0.1171875]},{"time":8.533333333333333,"value":1}]}
            ]}
            """);
        _app.Window.OpenTheme(_app.CreateTheme(), _basePath);
        return _app.Window.WallpapersTab.Parallax;
    }
}
