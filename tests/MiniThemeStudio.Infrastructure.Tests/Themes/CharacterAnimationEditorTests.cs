using System.Text.Json.Nodes;
using MiniThemeStudio.Core.Themes;
using MiniThemeStudio.Infrastructure.Themes;
using SkiaSharp;

namespace MiniThemeStudio.Infrastructure.Tests.Themes;

public sealed class CharacterAnimationEditorTests : IDisposable
{
    private const string RunAnimation = "/layout/lyt_hero@run.sceneanimation";

    private readonly string _rootPath = Directory.CreateTempSubdirectory().FullName;
    private readonly string _basePath;
    private readonly string _draftPath;
    private readonly string _baseDependencies;

    public CharacterAnimationEditorTests()
    {
        _basePath = Path.Combine(_rootPath, "base");
        _draftPath = Path.Combine(_rootPath, "draft");
        Directory.CreateDirectory(_draftPath);
        WriteFile("dependencies.json", """
            {"resources":[
              {"id":"scene","type":"Scene","dependencies":[{"id":"t1","refCount":1}],"path":"/prefab/demo.scn","groupId":"1"},
              {"id":"anim","type":"SceneAnimation","dependencies":[{"id":"t1","refCount":2},{"id":"t2","refCount":1}],"path":"/layout/lyt_hero@run.sceneanimation","groupId":"2"},
              {"id":"t1","type":"TextureResource","path":"/layout/asset/sprite/hero/hero_run01.png","groupId":"3"},
              {"id":"t2","type":"TextureResource","path":"/layout/asset/sprite/hero/hero_run02.png","groupId":"4"},
              {"id":"t1.frame.png","type":"SpriteResource","dependencies":[{"id":"t1","refCount":1}],"content":"{\"textureId\":\"t1\",\"textureFrame\":[0,0,8,8],\"pivot\":[4,4]}","contentType":1,"groupId":"5"}
            ],"resourceGroups":[]}
            """);
        WriteFile("resources/prefab/demo.scn", """
            {"rootWorldNode":{"name":"demo","components":[],"children":[
              {"name":"Sprite_Hero_0","components":[{"type":"TextureComponent","textureId":"t1","size":[72,96],"pivot":[0.5,0.5]}],"children":[]}
            ]}}
            """);
        WriteFile("resources/layout/lyt_hero@run.sceneanimation", """
            {"duration":0.1,"animations":[
              {"worldNodePath":["Sprite_Hero_0"],"type":"float","propertyId":"LocalScaleX","keyCurves":[{"time":0,"value":1}]},
              {"worldNodePath":["N","Sprite_Hero_0"],"type":"resource","componentType":"TextureComponent","propertyId":"Texture","keyCurves":[{"time":0,"value":"t1"},{"time":0.05,"value":"t2"},{"time":0.2,"value":"t1"}]}
            ]}
            """);
        WriteFile("resources/layout/asset/sprite/hero/hero_run01.json", """{"texture":{"id":"t1","width":24,"height":32},"sprites":{}}""");
        WriteFile("resources/layout/asset/sprite/hero/hero_run02.json", """{"texture":{"id":"t2","width":24,"height":32},"sprites":{}}""");
        SavePng(Path.Combine(_basePath, "resources", "layout", "asset", "sprite", "hero", "hero_run01.png"), 24, 32);
        SavePng(Path.Combine(_basePath, "resources", "layout", "asset", "sprite", "hero", "hero_run02.png"), 24, 32);
        _baseDependencies = File.ReadAllText(Path.Combine(_basePath, "dependencies.json"));
    }

    public void Dispose()
    {
        Directory.Delete(_rootPath, true);
    }

    [Fact]
    public void ReadsCharactersAnimationsAndNodes()
    {
        var editor = new CharacterAnimationEditor();

        CharacterAnimation animation = Assert.Single(editor.GetAnimations(_draftPath, _basePath, "hero"));
        CharacterNode node = Assert.Single(editor.GetNodes(_draftPath, _basePath, "hero"));

        Assert.Equal(["hero"], editor.GetCharacters(_draftPath, _basePath));
        Assert.Equal("run", animation.Name);
        Assert.Equal(0.1, animation.Duration);
        Assert.Equal(["t1", "t2", "t1"], Assert.Single(animation.Tracks).Keys.Select(key => key.TextureId));
        Assert.Equal(new CharacterNode("demo.scn", "Sprite_Hero_0", 72, 96, 0.5, 0.5), node);
    }

    [Fact]
    public void AddTextureRegistersImageAndSheetInDraftOnly()
    {
        var editor = new CharacterAnimationEditor();
        string source = Path.Combine(_rootPath, "frame.png");
        SavePng(source, 35, 43);

        string id = editor.AddTexture(_draftPath, _basePath, "resources/layout/asset/sprite/hero/hero_run03.png", source);

        JsonArray resources = ReadJson(Path.Combine(_draftPath, "dependencies.json"))["resources"]!.AsArray();
        JsonNode texture = resources.Single(resource => resource!["id"]!.GetValue<string>() == id)!;
        JsonNode sheet = resources.Single(resource => resource!["id"]!.GetValue<string>() == $"sheet_{id}")!;
        Assert.Equal("/layout/asset/sprite/hero/hero_run03.png", texture["path"]!.GetValue<string>());
        Assert.Equal("6", texture["groupId"]!.GetValue<string>());
        Assert.Equal("7", sheet["groupId"]!.GetValue<string>());
        Assert.Equal(id, ReadJson(Path.Combine(_draftPath, "resources", "layout", "asset", "sprite", "hero", "hero_run03.json"))["texture"]!["id"]!.GetValue<string>());
        Assert.Equal(_baseDependencies, File.ReadAllText(Path.Combine(_basePath, "dependencies.json")));
        Assert.Throws<IOException>(() => editor.AddTexture(_draftPath, _basePath, "resources/layout/asset/sprite/hero/hero_run03.png", source));
    }

    [Fact]
    public void MirrorTexturesFlipsEachTextureOnceIntoTheDraft()
    {
        var editor = new CharacterAnimationEditor();
        string basePng = Path.Combine(_basePath, "resources", "layout", "asset", "sprite", "hero", "hero_run01.png");
        using (var bitmap = new SKBitmap(new SKImageInfo(24, 32, SKColorType.Rgba8888, SKAlphaType.Unpremul)))
        {
            bitmap.Erase(SKColors.Transparent);
            bitmap.SetPixel(0, 31, SKColors.Red);
            bitmap.SetPixel(1, 0, new SKColor(0, 0, 255, 128));
            AtlasLayoutMatcherTests.SavePng(bitmap, basePng);
        }

        byte[] baseBytes = File.ReadAllBytes(basePng);

        editor.MirrorTextures(_draftPath, _basePath, ["t1", "t1"]);

        using var mirrored = SKBitmap.Decode(Path.Combine(_draftPath, "resources", "layout", "asset", "sprite", "hero", "hero_run01.png"));
        Assert.Equal((24, 32), (mirrored.Width, mirrored.Height));
        Assert.Equal(SKColors.Red, mirrored.GetPixel(23, 31));
        Assert.Equal(0, mirrored.GetPixel(0, 31).Alpha);
        Assert.Equal(new SKColor(0, 0, 255, 128), mirrored.GetPixel(22, 0));
        Assert.Equal(baseBytes, File.ReadAllBytes(basePng));
        Assert.False(File.Exists(Path.Combine(_draftPath, "resources", "layout", "asset", "sprite", "hero", "hero_run02.png")));
        Assert.Throws<InvalidDataException>(() => editor.MirrorTextures(_draftPath, _basePath, ["missing"]));
    }

    [Fact]
    public void SetTrackKeysRewritesKeysDurationAndReferenceCounts()
    {
        var editor = new CharacterAnimationEditor();

        editor.SetTrackKeys(_draftPath, _basePath, RunAnimation, "Sprite_Hero_0", [new(0.025, "t2"), new(0, "t1"), new(0.05, "t2")], 0.075);

        JsonNode animation = ReadJson(Path.Combine(_draftPath, "resources", "layout", "lyt_hero@run.sceneanimation"));
        string keys = animation["animations"]![1]!["keyCurves"]!.ToJsonString();
        string dependencies = ReadJson(Path.Combine(_draftPath, "dependencies.json"))["resources"]![1]!["dependencies"]!.ToJsonString();
        Assert.Equal("""[{"time":0,"value":"t1"},{"time":0.025,"value":"t2"},{"time":0.05,"value":"t2"}]""", keys);
        Assert.Equal(0.075, animation["duration"]!.GetValue<double>());
        Assert.Equal("""[{"id":"t1","refCount":1},{"id":"t2","refCount":2}]""", dependencies);
        string animationText = File.ReadAllText(Path.Combine(_draftPath, "resources", "layout", "lyt_hero@run.sceneanimation"));
        string dependenciesText = File.ReadAllText(Path.Combine(_draftPath, "dependencies.json"));
        Assert.Contains("\n    \"duration\"", animationText);
        Assert.DoesNotContain('\r', animationText);
        Assert.Contains("\"content\":\"{\\\"textureId\\\":\\\"t1\\\"", dependenciesText);
        Assert.DoesNotContain("\\u0022", dependenciesText);
        Assert.Throws<InvalidDataException>(() =>
            editor.SetTrackKeys(_draftPath, _basePath, RunAnimation, "Sprite_Hero_0", [new(0, "missing")], 0.1));
    }

    [Fact]
    public void SetFrameSizeScalesNodesAndKeepsTheFeetInPlace()
    {
        var editor = new CharacterAnimationEditor();

        editor.SetFrameSize(_draftPath, _basePath, "hero", new AtlasSize(35, 43));

        CharacterNode node = Assert.Single(editor.GetNodes(_draftPath, _basePath, "hero"));
        Assert.Equal((105, 129), (node.Width, node.Height));
        Assert.Equal(48, (1 - node.PivotY) * node.Height, 6);
        Assert.Equal(0.5, node.PivotX);
    }

    private void WriteFile(string relativePath, string content)
    {
        string path = Path.Combine(_basePath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static JsonNode ReadJson(string path) => JsonNode.Parse(File.ReadAllText(path))!;

    internal static void SavePng(string path, int width, int height)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.Yellow);
        AtlasLayoutMatcherTests.SavePng(bitmap, path);
    }
}
