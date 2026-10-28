using System.Text.Json.Nodes;
using MiniThemeStudio.Core.Scenes;
using MiniThemeStudio.Infrastructure.Themes;
using SkiaSharp;

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
            {"rootWorldNode":{"id":"r","name":"BG","transform":[1,0,0,0,1,0],"enabled":true,"zIndex":-1,"visible":true,"category":0,"components":[],"properties":{"wall_node":{"linkType":"WorldNode","linkId":"a"}},"children":[
              {"id":"a","name":"wall","transform":[1,0,0,0,1,-3],"zIndex":0,"visible":true,"unknownField":{"keep":[1,2.50]},"components":[
                {"type":"TextureComponent","textureId":"{{{WallId}}}","size":[1536,552],"pivot":[0.5,0.5],"uvRepeat":[2,1],"color":[1,1,1,0.5],"layer":0}],"children":[]},
              {"id":"b","name":"Pane","transform":[1,0,768,0,1,-131.5],"zIndex":1,"visible":false,"components":[{"type":"AnimatorComponent","id":"anim"}],"properties":{"cursor":{"linkType":"WorldNode","linkId":"c"},"scene":{"linkType":"WorldNode","linkId":"r"}},"children":[
                {"id":"c","name":"cursor","transform":[1,0,0,0,1,0],"components":[{"type":"SpriteComponent","spriteId":"{{{PackedId}}}.cursor_7.png","size":[32,32],"hFlip":true}],"children":[]},
                {"id":"d","name":"title","components":[{"type":"LabelComponent","text":"Hello"}],"children":[]}
              ]}
            ]}}
            """);
        WriteFile(_basePath, "resources/layout/asset/sprite/wallpaper/wall.json", $$$"""{"texture":{"id":"{{{WallId}}}","width":640,"height":360},"sprites":{}}""");
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
        SceneDocument scene = new SceneEditor().Load(_themePath, _basePath, "/prefab/bg.scn");

        Assert.False(scene.IsInTheme);
        Assert.Equal("BG", scene.Root.Name);
        Assert.Equal(-1, scene.Root.ZIndex);

        SceneNode wall = scene.Find("0")!;
        Assert.Equal(new SceneTransform(1, 0, 0, 0, 1, -3), wall.Transform);
        Assert.Equal(SceneVisualKind.Texture, wall.Visual!.Kind);
        Assert.Equal(WallId, wall.Visual.TextureId);
        Assert.Equal((1536d, 552d, 2d, 0.5), (wall.Visual.Width, wall.Visual.Height, wall.Visual.RepeatX, wall.Visual.Opacity));

        SceneNode pane = scene.Find("1")!;
        Assert.False(pane.Visible);
        Assert.Equal(768, pane.Transform.X);
        Assert.Equal(-131.5, pane.Transform.Y);
        Assert.Null(pane.Visual);
        Assert.Equal(["AnimatorComponent"], pane.ComponentTypes);

        SceneNode cursor = scene.Find("1/0")!;
        Assert.Equal(SceneVisualKind.Sprite, cursor.Visual!.Kind);
        Assert.Equal($"{PackedId}.cursor_7.png", cursor.Visual.SpriteId);
        Assert.True(cursor.Visual.HorizontalFlip);
        Assert.True(double.IsNaN(cursor.Visual.PivotX));

        SceneNode title = scene.Find("1/1")!;
        Assert.Equal("Hello", title.Visual!.Text);
        Assert.Equal(SceneTransform.Identity, title.Transform);
    }

    [Fact]
    public void EditWritesADraftCopyAndKeepsUnknownFields()
    {
        var editor = new SceneEditor();

        editor.EditNode(_themePath, _basePath, "/prefab/bg.scn", "0", new SceneNodeEdit(Name: "wall_far", X: 12, Y: 40.5, ZIndex: -2, Visible: false, Width: 1600));

        string draftScene = Path.Combine(_themePath, "resources", "prefab", "bg.scn");
        Assert.True(File.Exists(draftScene));
        Assert.DoesNotContain("wall_far", File.ReadAllText(Path.Combine(_basePath, "resources", "prefab", "bg.scn")));

        JsonNode wall = JsonNode.Parse(File.ReadAllText(draftScene))!["rootWorldNode"]!["children"]![0]!;
        Assert.Equal("wall_far", wall["name"]!.GetValue<string>());
        Assert.Equal("[1,0,12,0,1,40.5]", wall["transform"]!.ToJsonString());
        Assert.Equal(-2, wall["zIndex"]!.GetValue<int>());
        Assert.False(wall["visible"]!.GetValue<bool>());
        Assert.Equal("""{"keep":[1,2.50]}""", wall["unknownField"]!.ToJsonString());
        JsonNode component = wall["components"]![0]!;
        Assert.Equal("[1600,552]", component["size"]!.ToJsonString());
        Assert.Equal("[2,1]", component["uvRepeat"]!.ToJsonString());

        SceneDocument reloaded = editor.Load(_themePath, _basePath, "/prefab/bg.scn");
        Assert.True(reloaded.IsInTheme);
        Assert.Equal("wall_far", reloaded.Find("0")!.Name);
        Assert.False(File.Exists(Path.Combine(_themePath, "dependencies.json")));
    }

    [Fact]
    public void AddsATransformWhenTheNodeHasNone()
    {
        new SceneEditor().EditNode(_themePath, _basePath, "/prefab/bg.scn", "1/1", new SceneNodeEdit(Y: 10));

        SceneDocument scene = new SceneEditor().Load(_themePath, _basePath, "/prefab/bg.scn");
        Assert.Equal(new SceneTransform(1, 0, 0, 0, 1, 10), scene.Find("1/1")!.Transform);
    }

    [Fact]
    public void ChangingTheTextureMovesTheSceneReferenceCount()
    {
        new SceneEditor().EditNode(_themePath, _basePath, "/prefab/bg.scn", "0", new SceneNodeEdit(TextureId: NearId));

        JsonNode dependencies = JsonNode.Parse(File.ReadAllText(Path.Combine(_themePath, "dependencies.json")))!;
        JsonNode scene = dependencies["resources"]!.AsArray().First(resource => resource!["path"]?.GetValue<string>() == "/prefab/bg.scn")!;
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

    [Fact]
    public void AddsAnImageNodeOnTopOfItsSiblingsAtTheTextureSize()
    {
        var editor = new SceneEditor();

        string path = editor.AddImageNode(_themePath, _basePath, "/prefab/bg.scn", "", WallId, "wall");

        Assert.Equal("2", path);
        SceneNode node = editor.Load(_themePath, _basePath, "/prefab/bg.scn").Find(path)!;
        Assert.Equal("wall 2", node.Name);
        Assert.Equal(2, node.ZIndex);
        Assert.Equal(SceneTransform.Identity, node.Transform);
        Assert.Equal((SceneVisualKind.Texture, WallId, 640d, 360d), (node.Visual!.Kind, node.Visual.TextureId, node.Visual.Width, node.Visual.Height));
        Assert.Equal(3, SceneReferences()[WallId]);
    }

    [Fact]
    public void ImportsAPngAsANewTextureOfTheTheme()
    {
        string source = Path.Combine(_themePath, "My Logo!.png");
        using (var bitmap = new SKBitmap(24, 12))
        using (SKData png = SKImage.FromBitmap(bitmap).Encode(SKEncodedImageFormat.Png, 100))
        using (FileStream file = File.Create(source))
        {
            png.SaveTo(file);
        }

        var editor = new SceneEditor();
        string first = editor.ImportTexture(_themePath, _basePath, source);
        string second = editor.ImportTexture(_themePath, _basePath, source);

        Assert.NotEqual(first, second);
        Assert.True(File.Exists(Path.Combine(_themePath, "resources", "layout", "asset", "sprite", "scene", "my_logo.png")));
        Assert.True(File.Exists(Path.Combine(_themePath, "resources", "layout", "asset", "sprite", "scene", "my_logo_2.png")));
        string path = editor.AddImageNode(_themePath, _basePath, "/prefab/bg.scn", "1", second, "logo");
        SceneNode node = editor.Load(_themePath, _basePath, "/prefab/bg.scn").Find(path)!;
        Assert.Equal("1/2", path);
        Assert.Equal((24d, 12d), (node.Visual!.Width, node.Visual.Height));
        Assert.Equal(1, SceneReferences()[second]);
    }

    [Fact]
    public void DuplicatesABranchWithNewIdsAndItsOwnLinks()
    {
        var editor = new SceneEditor();

        string path = editor.DuplicateNode(_themePath, _basePath, "/prefab/bg.scn", "1");

        Assert.Equal("2", path);
        JsonArray children = SceneRoot()["children"]!.AsArray();
        JsonNode copy = children[2]!;
        Assert.Equal("Pane copy", copy["name"]!.GetValue<string>());
        Assert.Equal("[1,0,784,0,1,-147.5]", copy["transform"]!.ToJsonString());
        string cursorId = copy["children"]![0]!["id"]!.GetValue<string>();
        Assert.NotEqual("c", cursorId);
        Assert.NotEqual("anim", copy["components"]![0]!["id"]!.GetValue<string>());
        Assert.Equal(cursorId, copy["properties"]!["cursor"]!["linkId"]!.GetValue<string>());
        Assert.Equal("r", copy["properties"]!["scene"]!["linkId"]!.GetValue<string>());
        Assert.Equal("c", children[1]!["properties"]!["cursor"]!["linkId"]!.GetValue<string>());
        Assert.Equal(1, SceneReferences()[PackedId]);

        string second = editor.DuplicateNode(_themePath, _basePath, "/prefab/bg.scn", "1");
        Assert.Equal("Pane copy 2", editor.Load(_themePath, _basePath, "/prefab/bg.scn").Find(second)!.Name);
    }

    [Fact]
    public void DeletesABranchAndItsReferences()
    {
        var editor = new SceneEditor();
        editor.DuplicateNode(_themePath, _basePath, "/prefab/bg.scn", "0");
        Assert.Equal(3, SceneReferences()[WallId]);

        editor.DeleteNode(_themePath, _basePath, "/prefab/bg.scn", "2");

        SceneDocument scene = editor.Load(_themePath, _basePath, "/prefab/bg.scn");
        Assert.Equal(["wall", "wall copy"], scene.Root.Children.Select(child => child.Name));
        editor.DeleteNode(_themePath, _basePath, "/prefab/bg.scn", "1");
        Assert.Equal(2, SceneReferences()[WallId]);
    }

    [Fact]
    public void KeepsNodesTheSceneLinksToAndTheRoot()
    {
        var editor = new SceneEditor();

        InvalidDataException linked = Assert.Throws<InvalidDataException>(() => editor.DeleteNode(_themePath, _basePath, "/prefab/bg.scn", "0"));
        Assert.Contains("BG links to it", linked.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => editor.DeleteNode(_themePath, _basePath, "/prefab/bg.scn", "1/0"));
        Assert.Throws<ArgumentException>(() => editor.DeleteNode(_themePath, _basePath, "/prefab/bg.scn", ""));
        Assert.Throws<ArgumentException>(() => editor.DuplicateNode(_themePath, _basePath, "/prefab/bg.scn", ""));
        Assert.Throws<InvalidDataException>(() => editor.DuplicateNode(_themePath, _basePath, "/prefab/bg.scn", "9"));
        Assert.Throws<InvalidDataException>(() => editor.AddImageNode(_themePath, _basePath, "/prefab/bg.scn", "", "nope", "x"));
        Assert.False(File.Exists(Path.Combine(_themePath, "resources", "prefab", "bg.scn")));
    }

    [Fact]
    public void FindsTheAnimationsTheSceneCanPlay()
    {
        string dependencies = File.ReadAllText(Path.Combine(_basePath, "dependencies.json"));
        WriteFile(_basePath, "dependencies.json", dependencies.Replace(
            """{"id":"missing",""",
            """{"id":"blink","type":"SceneAnimation","path":"/layout/lyt_blink.sceneanimation","groupId":"7"},{"id":"other","type":"SceneAnimation","path":"/layout/lyt_other.sceneanimation","groupId":"8"},{"id":"missing",""",
            StringComparison.Ordinal));
        WriteFile(_basePath, "resources/layout/lyt_blink.sceneanimation", """
            {"duration":0.5,"animations":[
              {"worldNodePath":["cursor"],"type":"float","propertyId":"LocalPositionX","keyCurves":[{"time":0,"interp":"linear","value":[0,40]},{"time":0.5,"value":40}]},
              {"worldNodePath":["title"],"type":"bool","propertyId":"Visible","keyCurves":[{"time":0,"value":true},{"time":0.25,"value":false}]},
              {"worldNodePath":["title"],"type":"resource","componentType":"TextureComponent","propertyId":"Texture","keyCurves":[{"time":0,"value":"t1"}]},
              {"worldNodePath":["cursor"],"type":"float","propertyId":"Alpha","keyCurves":[{"time":0,"interp":"hermite","value":[1,0,-2,-2]}]}
            ]}
            """);
        WriteFile(_basePath, "resources/layout/lyt_other.sceneanimation", """
            {"duration":1,"animations":[{"worldNodePath":["Sprite_Mario"],"type":"float","propertyId":"Alpha","keyCurves":[{"time":0,"value":1}]}]}
            """);
        var editor = new SceneEditor();

        SceneAnimationBinding binding = Assert.Single(editor.GetAnimations(_themePath, _basePath, editor.Load(_themePath, _basePath, "/prefab/bg.scn")));

        Assert.Equal(("lyt_blink", "1", "Pane"), (binding.Clip.Name, binding.AnimatorPath, binding.AnimatorName));
        Assert.Equal(0.5, binding.Clip.Duration);
        var keys = binding.Clip.Tracks.Select(track => track.Keys[0]).ToList();
        Assert.Equal(new CurveKey(0, CurveInterpolation.Linear, 0, 40), keys[0]);
        Assert.Equal(true, keys[1].Flag);
        Assert.Equal("t1", keys[2].Text);
        Assert.Equal(new CurveKey(0, CurveInterpolation.Hermite, 1, 0, -2, -2), keys[3]);
    }

    private JsonObject SceneRoot()
    {
        return JsonNode.Parse(File.ReadAllText(Path.Combine(_themePath, "resources", "prefab", "bg.scn")))!["rootWorldNode"]!.AsObject();
    }

    private Dictionary<string, int> SceneReferences()
    {
        JsonNode dependencies = JsonNode.Parse(File.ReadAllText(Path.Combine(_themePath, "dependencies.json")))!;
        return dependencies["resources"]!.AsArray()
            .First(resource => resource!["path"]?.GetValue<string>() == "/prefab/bg.scn")!["dependencies"]!.AsArray()
            .ToDictionary(reference => reference!["id"]!.GetValue<string>(), reference => reference!["refCount"]!.GetValue<int>());
    }

    private static void WriteFile(string root, string relativePath, string content)
    {
        string path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
