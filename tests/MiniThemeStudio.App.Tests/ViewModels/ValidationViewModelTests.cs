using MiniThemeStudio.App.Errors;
using MiniThemeStudio.App.Tests.TestDoubles;

namespace MiniThemeStudio.App.Tests.ViewModels;

public sealed class ValidationViewModelTests : IDisposable
{
    private readonly TestApp _app = new();

    public ValidationViewModelTests()
    {
        _app.Window.OpenTheme(_app.CreateTheme(), _app.CreateBase());
    }

    public void Dispose() => _app.Dispose();

    private string DraftPath => _app.Window.CurrentDraftPath!;

    [Fact]
    public void AThemeThatOnlyUsesItsBaseIsValid()
    {
        var validation = _app.Window.ValidationTab;

        validation.Run();

        Assert.True(validation.IsThemeValid);
        Assert.DoesNotContain(validation.ValidationIssues, issue => issue.IsError);
        Assert.Equal(validation.ValidationSummary, _app.Window.Status);
        validation.EnsureValidFor("exporting");
    }

    [Fact]
    public void ErrorsStopSavingAndExporting()
    {
        TestApp.WriteFile(DraftPath, "resources/layout/asset/sprite/wallpaper/wall.png", "not a png");
        _app.Session.Reload();
        var validation = _app.Window.ValidationTab;

        validation.Run();

        Assert.False(validation.IsThemeValid);
        Assert.Equal("Validation found errors that must be fixed.", validation.ValidationSummary);
        var failure = Assert.Throws<OperationFailedException>(() => validation.EnsureValidFor("exporting"));
        Assert.StartsWith("Fix 1 validation error(s) before exporting:", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMissingTextureCanBeGivenAnImage()
    {
        // the theme registers a texture whose PNG nobody added
        var dependencies = """
            {"resources":[
              {"id":"scene","type":"Scene","dependencies":[{"id":"wall","refCount":1}],"path":"/prefab/bg.scn","groupId":"1"},
              {"id":"wall","type":"TextureResource","path":"/layout/asset/sprite/wallpaper/wall.png","groupId":"2"},
              {"id":"logo","type":"TextureResource","path":"/layout/asset/sprite/extra/logo.png","groupId":"3"}
            ],"resourceGroups":[]}
            """;
        TestApp.WriteFile(DraftPath, "dependencies.json", dependencies);
        _app.Session.Reload();
        var validation = _app.Window.ValidationTab;
        Assert.True(validation.HasMissingTextures);
        Assert.Equal("logo", validation.SelectedMissingTexture?.TextureId);

        TestApp.WriteBytes(_app.Root, "logo.png", TestApp.Png);
        _app.Files.Paths.Enqueue(Path.Combine(_app.Root, "logo.png"));
        await validation.ProvideMissingTextureCommand.ExecuteAsync(null);

        Assert.Empty(_app.Errors.Failures.Select(failure => $"{failure.Failure}: {failure.Exception.Message}"));
        Assert.True(File.Exists(Path.Combine(DraftPath, "resources", "layout", "asset", "sprite", "extra", "logo.png")));
        Assert.False(validation.HasMissingTextures);
        Assert.True(_app.Window.HasUnsavedChanges);
    }

    [Fact]
    public async Task AnImageNoSceneDrawsIsListedAndCanBeDeleted()
    {
        TestApp.WriteBytes(DraftPath, "resources/layout/asset/sprite/extra/unused.png", TestApp.Png);
        _app.Session.Reload();
        var validation = _app.Window.ValidationTab;
        var unused = Assert.Single(validation.UnusedImages);
        Assert.Equal("layout/asset/sprite/extra/unused.png", unused.RelativePath);

        _app.Dialogs.ConfirmationAnswer = false;
        await validation.DeleteUnusedImageCommand.ExecuteAsync(null);
        Assert.True(validation.HasUnusedImages);

        _app.Dialogs.ConfirmationAnswer = true;
        await validation.DeleteUnusedImageCommand.ExecuteAsync(null);

        Assert.False(File.Exists(Path.Combine(DraftPath, "resources", "layout", "asset", "sprite", "extra", "unused.png")));
        Assert.False(validation.HasUnusedImages);
        Assert.Equal("Deleted unused.", _app.Window.Status);
    }
}
