using System.Text.Json.Nodes;
using MiniThemeStudio.App.Models;
using MiniThemeStudio.App.Tests.TestDoubles;
using MiniThemeStudio.App.ViewModels;
using MiniThemeStudio.Core.Themes;
using SkiaSharp;

namespace MiniThemeStudio.App.Tests.ViewModels;

public sealed class CharactersViewModelTests : IDisposable
{
    private const string RunAnimation = "resources/layout/lyt_hero@run.sceneanimation";

    private readonly TestApp _app = new();

    // a hero running on two 24x32 frames for 0.1 s, then back to the first frame after the loop
    public CharactersViewModelTests()
    {
        string basePath = _app.CreateBase();
        TestApp.WriteFile(basePath, "dependencies.json", """
            {"resources":[
              {"id":"scene","type":"Scene","dependencies":[{"id":"wall","refCount":1}],"path":"/prefab/bg.scn","groupId":"1"},
              {"id":"wall","type":"TextureResource","path":"/layout/asset/sprite/wallpaper/wall.png","groupId":"2"},
              {"id":"demo","type":"Scene","dependencies":[{"id":"t1","refCount":1}],"path":"/prefab/demo.scn","groupId":"3"},
              {"id":"anim","type":"SceneAnimation","dependencies":[{"id":"t1","refCount":2},{"id":"t2","refCount":1}],"path":"/layout/lyt_hero@run.sceneanimation","groupId":"4"},
              {"id":"t1","type":"TextureResource","path":"/layout/asset/sprite/hero/hero_run01.png","groupId":"5"},
              {"id":"t2","type":"TextureResource","path":"/layout/asset/sprite/hero/hero_run02.png","groupId":"6"}
            ],"resourceGroups":[]}
            """);
        TestApp.WriteFile(basePath, "resources/prefab/demo.scn", """
            {"rootWorldNode":{"name":"demo","components":[],"children":[
              {"name":"Sprite_Hero_0","components":[{"type":"TextureComponent","textureId":"t1","size":[72,96],"pivot":[0.5,0.5]}],"children":[]}
            ]}}
            """);
        TestApp.WriteFile(basePath, RunAnimation, """
            {"duration":0.1,"animations":[
              {"worldNodePath":["N","Sprite_Hero_0"],"type":"resource","componentType":"TextureComponent","propertyId":"Texture","keyCurves":[{"time":0,"value":"t1"},{"time":0.05,"value":"t2"},{"time":0.2,"value":"t1"}]}
            ]}
            """);
        foreach ((string? frame, string? id) in new[] { ("hero_run01", "t1"), ("hero_run02", "t2") })
        {
            TestApp.WriteFile(basePath, $"resources/layout/asset/sprite/hero/{frame}.json", $$$"""{"texture":{"id":"{{{id}}}","width":24,"height":32},"sprites":{}}""");
            SavePng(Path.Combine(basePath, "resources", "layout", "asset", "sprite", "hero", $"{frame}.png"), 24, 32);
        }

        _app.Window.OpenTheme(_app.CreateTheme(), basePath);
    }

    public void Dispose() => _app.Dispose();

    private string DraftPath => _app.Window.CurrentDraftPath!;

    [Fact]
    public void ShowsTheRunAnimationWithItsFrames()
    {
        CharactersViewModel characters = _app.Window.CharactersTab;

        Assert.Equal(["hero"], characters.Characters);
        Assert.Equal("run", characters.SelectedAnimation?.Name);
        Assert.Equal(["hero_run01", "hero_run02", "hero_run01"], characters.AnimationFrames.Select(frame => frame.Name));
        Assert.Equal([true, true, false], characters.AnimationFrames.Select(frame => frame.IsLoopFrame));
        Assert.All(characters.AnimationFrames, frame => Assert.NotNull(frame.Thumbnail));
        Assert.Equal(50, characters.FrameTimeMilliseconds);
        Assert.Equal(new AtlasSize(24, 32), characters.CurrentCharacterFrameSize);
        Assert.Equal("Frames 24 x 32, drawn at 72 x 96 in demo.scn", characters.CharacterSizeText);
    }

    [Fact]
    public async Task ChangingTheFrameTimeRewritesTheAnimationInTheDraft()
    {
        CharactersViewModel characters = _app.Window.CharactersTab;
        characters.FrameTimeMilliseconds = 25;

        await characters.ApplyFrameTimeCommand.ExecuteAsync(null);

        Assert.Empty(_app.Errors.Failures.Select(failure => $"{failure.Failure}: {failure.Exception.Message}"));
        JsonNode animation = JsonNode.Parse(File.ReadAllText(Path.Combine(DraftPath, RunAnimation)))!;
        Assert.Equal(0.05, animation["duration"]!.GetValue<double>(), 6);
        Assert.Equal("run", characters.SelectedAnimation?.Name);
        Assert.Equal(25, characters.FrameTimeMilliseconds);
        Assert.Equal("hero run: 2 frames at 25 ms (0.05 s per loop).", _app.Window.Status);
    }

    [Fact]
    public async Task FlippingFramesTurnsOffTheMirroredPreview()
    {
        CharactersViewModel characters = _app.Window.CharactersTab;
        characters.IsPreviewMirrored = true;

        await characters.MirrorSelectedFramesCommand.ExecuteAsync(new List<object>());
        Assert.Equal("Select the frames to flip.", _app.Window.Status);

        await characters.MirrorAllFramesCommand.ExecuteAsync(null);

        Assert.Empty(_app.Errors.Failures.Select(failure => $"{failure.Failure}: {failure.Exception.Message}"));
        Assert.False(characters.IsPreviewMirrored);
        Assert.True(File.Exists(Path.Combine(DraftPath, "resources", "layout", "asset", "sprite", "hero", "hero_run02.png")));
        Assert.Equal("hero run: flipped 2 frames horizontally.", _app.Window.Status);
    }

    [Fact]
    public async Task NewFramesReplaceTheLoopAndAddTexturesForTheExtraOnes()
    {
        CharactersViewModel characters = _app.Window.CharactersTab;
        var frames = Enumerable.Range(1, 3).Select(index =>
        {
            string path = Path.Combine(_app.Root, $"new{index}.png");
            SavePng(path, 24, 32);
            return path;
        }).ToList();
        _app.AppDialogs.SpriteSheet = new SpriteSheetResult(frames, new AtlasSize(24, 32));

        await characters.ReplaceFramesCommand.ExecuteAsync(null);

        Assert.Empty(_app.Errors.Failures.Select(failure => $"{failure.Failure}: {failure.Exception.Message}"));
        Assert.Empty(_app.Dialogs.Shown);
        Assert.Equal(3, characters.AnimationFrames.Count(frame => frame.IsLoopFrame));
        Assert.True(File.Exists(Path.Combine(DraftPath, "resources", "layout", "asset", "sprite", "hero", "hero_run03.png")));
        Assert.StartsWith("hero run: 3 frames at 50 ms.", _app.Window.Status, StringComparison.Ordinal);
    }

    [Fact]
    public void SimpleEditingListsEveryFrameOfTheCharacter()
    {
        CharactersViewModel characters = _app.Window.CharactersTab;

        Assert.Equal(["Run 1", "Run 2"], characters.CharacterFrames.Select(frame => frame.Label));
        Assert.Equal("24 × 32", characters.CharacterFrames[0].SizeText);
        Assert.All(characters.CharacterFrames, frame => Assert.NotNull(frame.Thumbnail));
    }

    [Fact]
    public async Task AFrameIsReplacedByAPictureOfItsSize()
    {
        CharactersViewModel characters = _app.Window.CharactersTab;
        string wrongSize = Path.Combine(_app.Root, "big.png");
        SavePng(wrongSize, 30, 40);
        _app.Files.Paths.Enqueue(wrongSize);

        await characters.ReplaceFrameCommand.ExecuteAsync(characters.CharacterFrames[1]);

        (Exception? exception, string _) = Assert.Single(_app.Errors.Failures);
        Assert.Equal("Run 2 must be 24 × 32 pixels, like the frame it replaces; big.png is 30 × 40.", exception.Message);

        string rightSize = Path.Combine(_app.Root, "run2.png");
        SavePng(rightSize, 24, 32);
        _app.Files.Paths.Enqueue(rightSize);
        await characters.ReplaceFrameCommand.ExecuteAsync(characters.CharacterFrames[1]);

        Assert.Single(_app.Errors.Failures);
        Assert.True(File.Exists(Path.Combine(DraftPath, "resources", "layout", "asset", "sprite", "hero", "hero_run02.png")));
        Assert.Equal("hero: replaced Run 2.", _app.Window.Status);
        Assert.Equal(2, characters.CharacterFrames.Count);
    }

    private static void SavePng(string path, int width, int height)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.Red);
        using SKData png = SKImage.FromBitmap(bitmap).Encode(SKEncodedImageFormat.Png, 100);
        using FileStream file = File.Create(path);
        png.SaveTo(file);
    }
}
