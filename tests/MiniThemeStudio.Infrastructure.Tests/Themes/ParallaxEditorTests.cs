using System.Text.Json.Nodes;
using MiniThemeStudio.Core.Scenes;
using MiniThemeStudio.Infrastructure.Themes;
using SkiaSharp;

namespace MiniThemeStudio.Infrastructure.Tests.Themes;

public sealed class ParallaxEditorTests : IDisposable
{
    private const string Animation = "/layout/lyt_wallpaper.sceneanimation";

    private readonly string _themePath = Directory.CreateTempSubdirectory().FullName;
    private readonly string _basePath = Directory.CreateTempSubdirectory().FullName;
    private readonly ParallaxEditor _editor = new(new SceneEditor());

    // the SNES demo background: two walls sliding over an 8.5 s cycle, the near one twice as wide
    public ParallaxEditorTests()
    {
        WriteFile(_basePath, "dependencies.json", """
            {"resources":[
              {"id":"scene","type":"Scene","dependencies":[{"id":"far","refCount":1},{"id":"near","refCount":1}],"path":"/prefab/bg.scn","groupId":"1"},
              {"id":"anim","type":"SceneAnimation","path":"/layout/lyt_wallpaper.sceneanimation","groupId":"2"},
              {"id":"still","type":"SceneAnimation","path":"/layout/lyt_still.sceneanimation","groupId":"3"},
              {"id":"far","type":"TextureResource","path":"/layout/asset/sprite/wallpaper/wall_far.png","groupId":"4"},
              {"id":"near","type":"TextureResource","path":"/layout/asset/sprite/wallpaper/wall_near.png","groupId":"5"}
            ],"resourceGroups":[]}
            """);
        WriteFile(_basePath, "resources/prefab/bg.scn", """
            {"rootWorldNode":{"id":"r","name":"BG","components":[],"children":[
              {"id":"cam","name":"Camera","components":[{"type":"CameraComponent"}],"children":[]},
              {"id":"demo","name":"demo_bg","components":[],"children":[
                {"id":"pane","name":"RootPane","components":[{"type":"AnimatorComponent"}],"children":[
                  {"id":"n","name":"wall_near","transform":[1,0,768,0,1,-131.5],"zIndex":0,"components":[{"type":"TextureComponent","textureId":"near","size":[3072,300],"uvRepeat":[1,1]}],"children":[]},
                  {"id":"f","name":"wall_far","transform":[1,0,0,0,1,1.5],"zIndex":-1,"components":[{"type":"TextureComponent","textureId":"far","size":[1536,543],"uvRepeat":[1,1]}],"children":[]}
                ]}
              ]}
            ]}}
            """);
        WriteFile(_basePath, "resources" + Animation, """
            {"duration":8.533333333333333,"animations":[
              {"worldNodePath":["wall_far"],"type":"float","componentType":"TextureComponent","propertyId":"TextureOffsetX","keyCurves":[{"time":0,"interp":"hermite","value":[0,1,0.1171875,0.1171875]},{"time":8.533333333333333,"value":1}]},
              {"worldNodePath":["wall_near"],"type":"float","componentType":"TextureComponent","propertyId":"TextureOffsetX","keyCurves":[{"time":0,"interp":"hermite","value":[0,1,0.1171875,0.1171875]},{"time":8.533333333333333,"value":1}]}
            ]}
            """);
        WriteFile(_basePath, "resources/layout/lyt_still.sceneanimation", """
            {"duration":1,"animations":[{"worldNodePath":["Camera"],"type":"float","propertyId":"LocalPositionX","keyCurves":[{"time":0,"value":0}]}]}
            """);
    }

    public void Dispose()
    {
        Directory.Delete(_themePath, true);
        Directory.Delete(_basePath, true);
    }

    [Fact]
    public void FindsTheScrollingLayersAndTheirSpeed()
    {
        var background = Assert.Single(_editor.GetBackgrounds(_themePath, _basePath));

        Assert.Equal(("/prefab/bg.scn", Animation, "1/0", "RootPane"), (background.SceneResourcePath, background.AnimationResourcePath, background.AnimatorPath, background.AnimatorName));
        Assert.Equal("demo_bg/RootPane (lyt_wallpaper)", background.DisplayName);
        Assert.Equal(["wall_far", "wall_near"], background.Layers.Select(layer => layer.Name));
        var far = background.Layers[0];
        Assert.Equal(("1/0/1", -1, 1.5, 1536d, 1d), (far.NodePath, far.ZIndex, far.Y, far.TileWidth, far.LoopsPerCycle));
        Assert.Equal(180, far.PixelsPerSecond, 3);
        Assert.Equal(360, background.Layers[1].PixelsPerSecond, 3);
    }

    [Fact]
    public void ABaseWithoutAScrollingBackgroundHasNone()
    {
        File.Delete(Path.Combine(_basePath, "resources", "layout", "lyt_wallpaper.sceneanimation"));

        Assert.Empty(_editor.GetBackgrounds(_themePath, _basePath));
    }

    [Fact]
    public void ANewLayerCoversTheScreenAndScrollsWithTheOthers()
    {
        var source = Path.Combine(_themePath, "Hills.png");
        SavePng(source, 100, 40);
        var background = _editor.GetBackgrounds(_themePath, _basePath)[0];

        var path = _editor.AddLayer(_themePath, _basePath, background, new NewParallaxLayer("hills", source, -200, -2, 3, 2));

        Assert.Equal("1/0/2", path);
        var layer = _editor.GetBackgrounds(_themePath, _basePath)[0].Layers[0];
        Assert.Equal(("hills", -2, -200d, 2d), (layer.Name, layer.ZIndex, layer.Y, layer.LoopsPerCycle));

        // 100 px at 3x is 300 px: the PNG repeats it five times to cover the 1280 px screen
        Assert.Equal(1500, layer.TileWidth);
        var image = Path.Combine(_themePath, "resources", "layout", "asset", "sprite", "wallpaper", "hills.png");
        using (var tiled = SKBitmap.Decode(image))
        {
            Assert.Equal((500, 40), (tiled.Width, tiled.Height));
        }

        var node = new SceneEditor().Load(_themePath, _basePath, "/prefab/bg.scn").Find(path)!;
        Assert.Equal((1500d, 120d, 1d), (node.Visual!.Width, node.Visual.Height, node.Visual.RepeatX));
        var track = ReadJson(_themePath, "resources" + Animation)["animations"]!.AsArray()[2]!;
        Assert.Equal("""["hills"]""", track["worldNodePath"]!.ToJsonString());
        Assert.Equal(2, track["keyCurves"]![1]!["value"]!.GetValue<double>());
        var scene = ReadJson(_themePath, "dependencies.json")["resources"]!.AsArray().First(resource => resource!["id"]!.GetValue<string>() == "scene")!;
        Assert.Equal(3, scene["dependencies"]!.AsArray().Count);
    }

    [Fact]
    public void TheSpeedOfALayerCanChange()
    {
        var background = _editor.GetBackgrounds(_themePath, _basePath)[0];

        _editor.SetSpeed(_themePath, _basePath, background, "wall_far", -3);

        var far = _editor.GetBackgrounds(_themePath, _basePath)[0].Layers[0];
        Assert.Equal(-3, far.LoopsPerCycle);
        Assert.Equal(-540, far.PixelsPerSecond, 3);
        Assert.Throws<InvalidDataException>(() => _editor.SetSpeed(_themePath, _basePath, background, "Camera", 1));
    }

    [Fact]
    public void RemovingALayerDropsItsNodeAndItsTrack()
    {
        var source = Path.Combine(_themePath, "hills.png");
        SavePng(source, 640, 40);
        _editor.AddLayer(_themePath, _basePath, _editor.GetBackgrounds(_themePath, _basePath)[0], new NewParallaxLayer("hills", source, 0, 1, 3, 1));
        var background = _editor.GetBackgrounds(_themePath, _basePath)[0];

        _editor.RemoveLayer(_themePath, _basePath, background, "hills");

        var after = _editor.GetBackgrounds(_themePath, _basePath)[0];
        Assert.Equal(["wall_far", "wall_near"], after.Layers.Select(layer => layer.Name));
        Assert.Equal(2, ReadJson(_themePath, "resources" + Animation)["animations"]!.AsArray().Count);
        Assert.Equal(2, new SceneEditor().Load(_themePath, _basePath, "/prefab/bg.scn").Find("1/0")!.Children.Count);
    }

    private static JsonNode ReadJson(string root, string relativePath) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(root, relativePath)))!;

    private static void SavePng(string path, int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.SkyBlue);
        using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        using var file = File.Create(path);
        png.SaveTo(file);
    }

    private static void WriteFile(string root, string relativePath, string content)
    {
        var path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
