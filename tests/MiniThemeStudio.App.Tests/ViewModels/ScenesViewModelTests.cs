using MiniThemeStudio.App.Controls;
using MiniThemeStudio.App.Tests.TestDoubles;
using MiniThemeStudio.App.ViewModels;

namespace MiniThemeStudio.App.Tests.ViewModels;

public sealed class ScenesViewModelTests : IDisposable
{
    private readonly TestApp _app = new();

    public ScenesViewModelTests()
    {
        string basePath = _app.CreateBase();
        _app.Window.OpenTheme(_app.CreateTheme(), basePath);
    }

    public void Dispose() => _app.Dispose();

    [Fact]
    public void SelectingANodeFillsItsFields()
    {
        ScenesViewModel scenes = _app.Window.ScenesTab;

        scenes.SelectedScenePath = "0";

        Assert.Equal("wall", scenes.SceneNodeName);
        Assert.Equal(0, scenes.SceneNodeX);
        Assert.Equal(-3, scenes.SceneNodeY);
        Assert.True(scenes.SelectedNodeIsTexture);
        Assert.Equal("wall", scenes.SceneNodeTexture?.TextureId);
    }

    [Fact]
    public async Task ApplyingWritesTheSceneToTheDraftAndMarksTheThemeUnsaved()
    {
        ScenesViewModel scenes = _app.Window.ScenesTab;
        scenes.SelectedScenePath = "0";
        scenes.SceneNodeName = "wall_far";
        scenes.SceneNodeY = 12;

        await scenes.ApplyNodeChangesCommand.ExecuteAsync(null);

        Assert.Empty(_app.Errors.Failures.Select(failure => $"{failure.Failure}: {failure.Exception.Message}"));
        Assert.True(_app.Window.HasUnsavedChanges);
        string draftScene = File.ReadAllText(Path.Combine(_app.Window.CurrentDraftPath!, "resources", "prefab", "bg.scn"));
        Assert.Contains("\"name\":\"wall_far\"", draftScene);
        Assert.Contains("\"transform\":[1,0,0,0,1,12]", draftScene);

        // the tab reloaded the scene and kept the node selected
        Assert.Equal("wall_far", scenes.SelectedSceneNode?.Name);
        Assert.EndsWith("updated wall_far.", _app.Window.Status);
    }

    [Fact]
    public async Task ApplyingWithoutChangesWritesNothing()
    {
        ScenesViewModel scenes = _app.Window.ScenesTab;
        scenes.SelectedScenePath = "0";

        await scenes.ApplyNodeChangesCommand.ExecuteAsync(null);

        Assert.False(_app.Window.HasUnsavedChanges);
        Assert.False(File.Exists(Path.Combine(_app.Window.CurrentDraftPath!, "resources", "prefab", "bg.scn")));
        Assert.Equal("wall: nothing changed.", _app.Window.Status);
    }

    [Fact]
    public void DiscardRestoresTheFieldsOfTheNode()
    {
        ScenesViewModel scenes = _app.Window.ScenesTab;
        scenes.SelectedScenePath = "0";
        scenes.SceneNodeX = 99;

        scenes.ResetSceneNodeFieldsCommand.Execute(null);

        Assert.Equal(0, scenes.SceneNodeX);
    }

    [Fact]
    public async Task AddingAnImageImportsThePngNextToTheSelectedImage()
    {
        ScenesViewModel scenes = _app.Window.ScenesTab;
        TestApp.WriteBytes(_app.Root, "logo.png", TestApp.Png);
        _app.Files.Paths.Enqueue(Path.Combine(_app.Root, "logo.png"));
        scenes.SelectedScenePath = "0";

        await scenes.AddImageCommand.ExecuteAsync(null);

        Assert.Empty(_app.Errors.Failures.Select(failure => $"{failure.Failure}: {failure.Exception.Message}"));
        Assert.Equal("1", scenes.SelectedScenePath);
        Assert.Equal("logo", scenes.SelectedSceneNode?.Name);
        Assert.True(scenes.SelectedNodeIsTexture);
        Assert.Equal("logo", scenes.SceneNodeTexture?.Name);
        Assert.True(File.Exists(Path.Combine(_app.Window.CurrentDraftPath!, "resources", "layout", "asset", "sprite", "scene", "logo.png")));
        Assert.True(_app.Window.HasUnsavedChanges);
        Assert.Equal("bg.scn: added logo.", _app.Window.Status);
    }

    [Fact]
    public async Task DuplicatingSelectsTheCopy()
    {
        ScenesViewModel scenes = _app.Window.ScenesTab;
        scenes.SelectedScenePath = "0";

        await scenes.DuplicateNodeCommand.ExecuteAsync(null);

        Assert.Equal("1", scenes.SelectedScenePath);
        Assert.Equal("wall copy", scenes.SceneNodeName);
        Assert.Equal(16, scenes.SceneNodeX);
        Assert.Equal(-19, scenes.SceneNodeY);
    }

    [Fact]
    public async Task DeletingAsksFirstAndThenSelectsTheParent()
    {
        ScenesViewModel scenes = _app.Window.ScenesTab;
        scenes.SelectedScenePath = "0";
        _app.Dialogs.ConfirmationAnswer = false;

        await scenes.DeleteNodeCommand.ExecuteAsync(null);

        Assert.Equal("Delete node", Assert.Single(_app.Dialogs.Shown).Title);
        Assert.Single(scenes.CurrentScene!.Root.Children);

        _app.Dialogs.ConfirmationAnswer = true;
        await scenes.DeleteNodeCommand.ExecuteAsync(null);

        Assert.Empty(scenes.CurrentScene!.Root.Children);
        Assert.Equal("", scenes.SelectedScenePath);
        Assert.Equal("bg.scn: deleted wall.", _app.Window.Status);
    }

    [Fact]
    public void TheRootCannotBeDuplicatedOrDeleted()
    {
        ScenesViewModel scenes = _app.Window.ScenesTab;

        scenes.SelectedScenePath = "";

        Assert.False(scenes.DuplicateNodeCommand.CanExecute(null));
        Assert.False(scenes.DeleteNodeCommand.CanExecute(null));
        Assert.True(scenes.AddImageCommand.CanExecute(null));
    }

    [Fact]
    public async Task DroppingADraggedNodeWritesItsPosition()
    {
        ScenesViewModel scenes = _app.Window.ScenesTab;
        scenes.SelectedScenePath = "0";

        await scenes.MoveNodeCommand.ExecuteAsync(new SceneNodeMove("0", 40, 25));

        Assert.Contains("\"transform\":[1,0,40,0,1,25]", File.ReadAllText(Path.Combine(_app.Window.CurrentDraftPath!, "resources", "prefab", "bg.scn")));
        Assert.Equal("0", scenes.SelectedScenePath);
        Assert.Equal(40, scenes.SceneNodeX);
    }

    [Fact]
    public void AnAnimationOfTheScenePlaysInThePreviewOnly()
    {
        // the wall slides 100 px to the right over one second
        TestApp.WriteFile(_app.Window.CurrentDraftPath!, "dependencies.json", """
            {"resources":[
              {"id":"scene","type":"Scene","dependencies":[{"id":"wall","refCount":1}],"path":"/prefab/bg.scn","groupId":"1"},
              {"id":"wall","type":"TextureResource","path":"/layout/asset/sprite/wallpaper/wall.png","groupId":"2"},
              {"id":"slide","type":"SceneAnimation","path":"/layout/lyt_slide.sceneanimation","groupId":"3"}
            ],"resourceGroups":[]}
            """);
        TestApp.WriteFile(_app.Window.CurrentDraftPath!, "resources/layout/lyt_slide.sceneanimation", """
            {"duration":1,"animations":[{"worldNodePath":["wall"],"type":"float","propertyId":"LocalPositionX","keyCurves":[{"time":0,"interp":"linear","value":[0,100]},{"time":1,"value":100}]}]}
            """);
        _app.Session.Reload();
        ScenesViewModel scenes = _app.Window.ScenesTab;
        Assert.Same(scenes.CurrentScene!.Root, scenes.PreviewRoot);

        scenes.SelectedAnimation = Assert.Single(scenes.SceneAnimations);
        scenes.Clock.Time = 0.25;

        Assert.Equal("", scenes.SelectedScenePath);
        Assert.Equal(25, scenes.PreviewRoot!.Children[0].Transform.X, 6);
        Assert.Equal(0, scenes.CurrentScene!.Root.Children[0].Transform.X);
        Assert.Equal("0.25 / 1.00 s", scenes.Clock.TimeText);

        scenes.Clock.TogglePlayCommand.Execute(null);
        Assert.True(scenes.Clock.IsPlaying);
        scenes.Clock.TogglePlayCommand.Execute(null);
        Assert.False(scenes.Clock.IsPlaying);

        scenes.StopAnimationCommand.Execute(null);

        Assert.Null(scenes.SelectedAnimation);
        Assert.Same(scenes.CurrentScene!.Root, scenes.PreviewRoot);
        Assert.False(scenes.Clock.TogglePlayCommand.CanExecute(null));
    }

    [Fact]
    public void ASpriteNodeAsksTheWindowToShowItsSprite()
    {
        // a scene node drawing an atlas sprite that no atlas of the theme has
        TestApp.WriteFile(_app.Window.CurrentDraftPath!, "resources/prefab/bg.scn", """
            {"rootWorldNode":{"name":"BG","children":[
              {"name":"cursor","components":[{"type":"SpriteComponent","spriteId":"atlas.cursor.png","size":[32,32]}],"children":[]}
            ]}}
            """);
        _app.Session.Reload();
        ScenesViewModel scenes = _app.Window.ScenesTab;
        scenes.SelectedScenePath = "0";
        _app.Window.SelectedTabIndex = 2;

        scenes.EditSpriteCommand.Execute(null);

        Assert.Equal("Sprite cursor.png was not found in the atlases.", _app.Window.Status);
        Assert.Equal(2, _app.Window.SelectedTabIndex);
    }
}
