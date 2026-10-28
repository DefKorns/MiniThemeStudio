using MiniThemeStudio.App.Tests.TestDoubles;

namespace MiniThemeStudio.App.Tests.ViewModels;

public sealed class ScenesViewModelTests : IDisposable
{
    private readonly TestApp _app = new();

    public ScenesViewModelTests()
    {
        var basePath = _app.CreateBase();
        _app.Window.OpenTheme(_app.CreateTheme(), basePath);
    }

    public void Dispose() => _app.Dispose();

    [Fact]
    public void SelectingANodeFillsItsFields()
    {
        var scenes = _app.Window.ScenesTab;

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
        var scenes = _app.Window.ScenesTab;
        scenes.SelectedScenePath = "0";
        scenes.SceneNodeName = "wall_far";
        scenes.SceneNodeY = 12;

        await scenes.ApplyNodeChangesCommand.ExecuteAsync(null);

        Assert.Empty(_app.Errors.Failures.Select(failure => $"{failure.Failure}: {failure.Exception.Message}"));
        Assert.True(_app.Window.HasUnsavedChanges);
        var draftScene = File.ReadAllText(Path.Combine(_app.Window.CurrentDraftPath!, "resources", "prefab", "bg.scn"));
        Assert.Contains("\"name\":\"wall_far\"", draftScene);
        Assert.Contains("\"transform\":[1,0,0,0,1,12]", draftScene);

        // the tab reloaded the scene and kept the node selected
        Assert.Equal("wall_far", scenes.SelectedSceneNode?.Name);
        Assert.EndsWith("updated wall_far.", _app.Window.Status);
    }

    [Fact]
    public async Task ApplyingWithoutChangesWritesNothing()
    {
        var scenes = _app.Window.ScenesTab;
        scenes.SelectedScenePath = "0";

        await scenes.ApplyNodeChangesCommand.ExecuteAsync(null);

        Assert.False(_app.Window.HasUnsavedChanges);
        Assert.False(File.Exists(Path.Combine(_app.Window.CurrentDraftPath!, "resources", "prefab", "bg.scn")));
        Assert.Equal("wall: nothing changed.", _app.Window.Status);
    }

    [Fact]
    public void DiscardRestoresTheFieldsOfTheNode()
    {
        var scenes = _app.Window.ScenesTab;
        scenes.SelectedScenePath = "0";
        scenes.SceneNodeX = 99;

        scenes.ResetSceneNodeFieldsCommand.Execute(null);

        Assert.Equal(0, scenes.SceneNodeX);
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
        var scenes = _app.Window.ScenesTab;
        scenes.SelectedScenePath = "0";
        _app.Window.SelectedTabIndex = 2;

        scenes.EditSpriteCommand.Execute(null);

        Assert.Equal("Sprite cursor.png was not found in the atlases.", _app.Window.Status);
        Assert.Equal(2, _app.Window.SelectedTabIndex);
    }
}
