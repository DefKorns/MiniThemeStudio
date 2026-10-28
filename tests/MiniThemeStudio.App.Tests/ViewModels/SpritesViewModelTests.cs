using MiniThemeStudio.App.Tests.TestDoubles;
using MiniThemeStudio.App.ViewModels;

namespace MiniThemeStudio.App.Tests.ViewModels;

public sealed class SpritesViewModelTests : IDisposable
{
    private readonly TestApp _app = new();
    private readonly string _basePath;

    public SpritesViewModelTests()
    {
        _basePath = _app.CreateBase();
        _app.Window.OpenTheme(_app.CreateTheme(), _basePath);
    }

    public void Dispose() => _app.Dispose();

    [Fact]
    public void ShowsTheAtlasOfTheBaseAndSelectsItsFirstSprite()
    {
        var sprites = _app.Window.SpritesTab;

        Assert.Equal("packed", sprites.SelectedAtlas?.Name);
        Assert.Equal("1 x 1", sprites.AtlasSummary);
        Assert.StartsWith(_basePath, sprites.AtlasImagePath, StringComparison.Ordinal);
        Assert.Equal("dot.png", sprites.SelectedSprite?.Name);
        Assert.True(sprites.CanReplaceSprite);
    }

    [Fact]
    public void SearchingFiltersTheSpritesByName()
    {
        var sprites = _app.Window.SpritesTab;

        sprites.SpriteSearchText = "DOT";
        Assert.Single(sprites.FilteredSprites);

        sprites.SpriteSearchText = "cursor";
        Assert.Empty(sprites.FilteredSprites);
    }

    [Fact]
    public async Task ReplacingASpriteCopiesTheBaseAtlasIntoTheDraftFirst()
    {
        var sprites = _app.Window.SpritesTab;
        TestApp.WriteBytes(_app.Root, "dot.png", TestApp.Png);
        _app.Files.Paths.Enqueue(Path.Combine(_app.Root, "dot.png"));
        var revision = sprites.AtlasRevision;

        await sprites.ReplaceSpriteCommand.ExecuteAsync(null);

        Assert.Empty(_app.Errors.Failures.Select(failure => $"{failure.Failure}: {failure.Exception.Message}"));
        var draftAtlas = Path.Combine(_app.Window.CurrentDraftPath!, "resources", "sprites", "packed.png");
        Assert.True(File.Exists(draftAtlas));
        Assert.Equal(Path.GetFullPath(draftAtlas), Path.GetFullPath(sprites.AtlasImagePath));
        Assert.Equal("dot.png", sprites.SelectedSprite?.Name);
        Assert.True(sprites.AtlasRevision > revision);
        Assert.True(_app.Window.HasUnsavedChanges);
        Assert.Equal("Replaced dot.png in draft.", _app.Window.Status);
    }

    [Fact]
    public async Task ExportingSavesTheSpriteAsAPng()
    {
        var sprites = _app.Window.SpritesTab;
        var target = Path.Combine(_app.Root, "dot_export.png");
        _app.Files.Paths.Enqueue(target);

        await sprites.ExportSpriteCommand.ExecuteAsync(null);

        Assert.True(File.Exists(target));
        Assert.Equal("Exported dot.png to dot_export.png.", _app.Window.Status);
    }

    [Fact]
    public void ASceneCanAskForOneOfTheSprites()
    {
        var sprites = _app.Window.SpritesTab;
        sprites.SpriteSearchText = "nothing";

        Assert.True(sprites.ShowSprite(new SceneSpriteRequest("packed", "dot.png", null)));

        Assert.Equal("", sprites.SpriteSearchText);
        Assert.Equal("dot.png", sprites.SelectedSprite?.Name);
        Assert.False(sprites.ShowSprite(new SceneSpriteRequest("packed", "gone.png", null)));
        Assert.Equal("Sprite gone.png was not found in the atlases.", _app.Window.Status);
    }
}
