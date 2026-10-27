using System.Text.Json.Nodes;
using MiniThemeStudio.Core.Scenes;
using MiniThemeStudio.Infrastructure.Themes;

namespace MiniThemeStudio.Infrastructure.Tests.Themes;

public sealed class SceneEditorTests : IDisposable
{
    private const string WallId = "1c75bca3-310d-48f1-95ad-20e7ebc294c6";
    private const string NearId = "3384d6e1-fad7-4b8d-9a5e-38ea786aaf28";
    private const string PackedId = "ff9fb46c-ad68-46f6-9e87-7108e2080773";

    private readonly string _themePath = Directory.CreateTempSubdirectory().FullName;
    private readonly string _basePath = Directory.CreateTempSubdirectory().FullName;

    public SceneEditorTests()
    {
        WriteFile(_basePath, "dependencies.json", $$$"""
            {"resources":[
              {"id":"scene","type":"Scene","dependencies":[{"id":"{{{WallId}}}","refCount":2},{"id":"other","refCount":1}],"path":"/prefab/bg.scn","groupId":"1"},
              {"id":"missing","type":"Scene","path":"/prefab/gone.scn","groupId":"2"},
              {"id":"{{{WallId}}}","type":"TextureResource","path":"/layout/asset/sprite/wallpaper/wall.png","groupId":"3"},
              {"id":"{{{NearId}}}","type":"TextureResource","path":"/layout/asset/sprite/wallpaper/wall_near.png","groupId":"4"}
            ],"resourceGroups":[]}
            """);
        WriteFile(_basePath, "resources/prefab/bg.scn", $$$"""
            {"rootWorldNode":{"id":"r","name":"BG","transform":[1,0,0,0,1,0],"enabled":true,"zIndex":-1,"visible":true,"category":0,"components":[],"children":[
              {"id":"a","name":"wall","transform":[1,0,0,0,1,-3],"zIndex":0,"visible":true,"unknownField":{"keep":[1,2.50]},"components":[
                {"type":"TextureComponent","textureId":"{{{WallId}}}","size":[1536,552],"pivot":[0.5,0.5],"uvRepeat":[2,1],"color":[1,1,1,0.5],"layer":0}],"children":[]},
              {"id":"b","name":"Pane","transform":[1,0,768,0,1,-131.5],"zIndex":1,"visible":false,"components":[{"type":"AnimatorComponent"}],"children":[
                {"id":"c","name":"cursor","transform":[1,0,0,0,1,0],"components":[{"type":"SpriteComponent","spriteId":"{{{PackedId}}}.cursor_7.png","size":[32,32],"hFlip":true}],"children":[]},
                {"id":"d","name":"title","components":[{"type":"LabelComponent","text":"Hello"}],"children":[]}
              ]}
            ]}}
            """);
    }

    public void Dispose()
    {
        Directory.Delete(_themePath, true);
        Directory.Delete(_basePath, true);
    }

    [Fact]
    public void ListsOnlyScenesWithAFile()
    {
        Assert.Equal(["/prefab/bg.scn"], new SceneEditor().GetScenes(_themePath, _basePath));
    }

    [Fact]
    public void LoadsTheNodeTreeWithTransformsAndVisuals()
    {
        var scene = new SceneEditor().Load(_themePath, _basePath, "/prefab/bg.scn");

        Assert.False(scene.IsInTheme);
        Assert.Equal("BG", scene.Root.Name);
        Assert.Equal(-1, scene.Root.ZIndex);

        var wall = scene.Find("0")!;
        Assert.Equal(new SceneTransform(1, 0, 0, 0, 1, -3), wall.Transform);
        Assert.Equal(SceneVisualKind.Texture, wall.Visual!.Kind);
        Assert.Equal(WallId, wall.Visual.TextureId);
        Assert.Equal((1536d, 552d, 2d, 0.5), (wall.Visual.Width, wall.Visual.Height, wall.Visual.RepeatX, wall.Visual.Opacity));

        var pane = scene.Find("1")!;
        Assert.False(pane.Visible);
        Assert.Equal(768, pane.Transform.X);
        Assert.Equal(-131.5, pane.Transform.Y);
        Assert.Null(pane.Visual);
        Assert.Equal(["AnimatorComponent"], pane.ComponentTypes);

        var cursor = scene.Find("1/0")!;
        Assert.Equal(SceneVisualKind.Sprite, cursor.Visual!.Kind);
        Assert.Equal($"{PackedId}.cursor_7.png", cursor.Visual.SpriteId);
        Assert.True(cursor.Visual.HorizontalFlip);
        Assert.True(double.IsNaN(cursor.Visual.PivotX));

        var title = scene.Find("1/1")!;
        Assert.Equal("Hello", title.Visual!.Text);
        Assert.Equal(SceneTransform.Identity, title.Transform);
    }

    [Fact]
    public void EditWritesADraftCopyAndKeepsUnknownFields()
    {
        var editor = new SceneEditor();

        editor.EditNode(_themePath, _basePath, "/prefab/bg.scn", "0", new SceneNodeEdit(Name: "wall_far", X: 12, Y: 40.5, ZIndex: -2, Visible: false, Width: 1600));

        var draftScene = Path.Combine(_themePath, "resources", "prefab", "bg.scn");
        Assert.True(File.Exists(draftScene));
        Assert.DoesNotContain("wall_far", File.ReadAllText(Path.Combine(_basePath, "resources", "prefab", "bg.scn")));

        var wall = JsonNode.Parse(File.ReadAllText(draftScene))!["rootWorldNode"]!["children"]![0]!;
        Assert.Equal("wall_far", wall["name"]!.GetValue<string>());
        Assert.Equal("[1,0,12,0,1,40.5]", wall["transform"]!.ToJsonString());
        Assert.Equal(-2, wall["zIndex"]!.GetValue<int>());
        Assert.False(wall["visible"]!.GetValue<bool>());
        Assert.Equal("""{"keep":[1,2.50]}""", wall["unknownField"]!.ToJsonString());
        var component = wall["components"]![0]!;
        Assert.Equal("[1600,552]", component["size"]!.ToJsonString());
        Assert.Equal("[2,1]", component["uvRepeat"]!.ToJsonString());

        var reloaded = editor.Load(_themePath, _basePath, "/prefab/bg.scn");
        Assert.True(reloaded.IsInTheme);
        Assert.Equal("wall_far", reloaded.Find("0")!.Name);
        Assert.False(File.Exists(Path.Combine(_themePath, "dependencies.json")));
    }

    [Fact]
    public void AddsATransformWhenTheNodeHasNone()
    {
        new SceneEditor().EditNode(_themePath, _basePath, "/prefab/bg.scn", "1/1", new SceneNodeEdit(Y: 10));

        var scene = new SceneEditor().Load(_themePath, _basePath, "/prefab/bg.scn");
        Assert.Equal(new SceneTransform(1, 0, 0, 0, 1, 10), scene.Find("1/1")!.Transform);
    }

    [Fact]
    public void ChangingTheTextureMovesTheSceneReferenceCount()
    {
        new SceneEditor().EditNode(_themePath, _basePath, "/prefab/bg.scn", "0", new SceneNodeEdit(TextureId: NearId));

        var dependencies = JsonNode.Parse(File.ReadAllText(Path.Combine(_themePath, "dependencies.json")))!;
        var scene = dependencies["resources"]!.AsArray().First(resource => resource!["path"]?.GetValue<string>() == "/prefab/bg.scn")!;
        Assert.Equal(
            $$$"""[{"id":"{{{WallId}}}","refCount":1},{"id":"other","refCount":1},{"id":"{{{NearId}}}","refCount":1}]""",
            scene["dependencies"]!.ToJsonString());
        Assert.Equal(NearId, new SceneEditor().Load(_themePath, _basePath, "/prefab/bg.scn").Find("0")!.Visual!.TextureId);
    }

    [Fact]
    public void RejectsUnregisteredTexturesAndSpriteNodes()
    {
        var editor = new SceneEditor();

        Assert.Throws<InvalidDataException>(() => editor.EditNode(_themePath, _basePath, "/prefab/bg.scn", "0", new SceneNodeEdit(TextureId: "nope")));
        Assert.Throws<InvalidDataException>(() => editor.EditNode(_themePath, _basePath, "/prefab/bg.scn", "1/0", new SceneNodeEdit(TextureId: NearId)));
        Assert.Throws<InvalidDataException>(() => editor.EditNode(_themePath, _basePath, "/prefab/bg.scn", "5", new SceneNodeEdit(X: 1)));
        Assert.Throws<ArgumentException>(() => editor.EditNode(_themePath, _basePath, "/prefab/bg.scn", "0", new SceneNodeEdit(Name: " ")));
        Assert.False(File.Exists(Path.Combine(_themePath, "resources", "prefab", "bg.scn")));
    }

    private static void WriteFile(string root, string relativePath, string content)
    {
        var path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
