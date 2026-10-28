using MiniThemeStudio.App.Dialogs;
using MiniThemeStudio.App.Errors;
using MiniThemeStudio.App.Tests.TestDoubles;

namespace MiniThemeStudio.App.Tests.ViewModels;

public sealed class MainWindowViewModelTests : IDisposable
{
    private readonly TestApp _app = new();
    private readonly string _basePath;
    private readonly string _themePath;

    public MainWindowViewModelTests()
    {
        _basePath = _app.CreateBase();
        _themePath = _app.CreateTheme();
    }

    public void Dispose() => _app.Dispose();

    [Fact]
    public void OpeningAThemeLoadsEveryTab()
    {
        var window = _app.Window;

        window.OpenTheme(_themePath, _basePath);

        Assert.True(window.IsThemeOpen);
        Assert.False(window.HasUnsavedChanges);
        Assert.Equal("Tester", window.Author);
        Assert.Equal(["/prefab/bg.scn"], window.ScenesTab.SceneResources);
        Assert.Contains(window.WallpapersTab.Wallpapers, wallpaper => wallpaper.Name == "wall");
        Assert.NotNull(window.CurrentDraftPath);
        Assert.StartsWith(_app.Root, window.CurrentDraftPath);
    }

    [Fact]
    public async Task AThemeOfNoKnownConsoleIsNotOpenedWithoutABase()
    {
        _app.Files.Paths.Enqueue(_themePath);

        await _app.Window.OpenThemeCommand.ExecuteAsync(null);

        Assert.False(_app.Window.IsThemeOpen);
        Assert.Equal(1, _app.AppDialogs.ManageBasesCalls);
        Assert.Empty(_app.Errors.Failures.Select(failure => $"{failure.Failure}: {failure.Exception.Message}"));
    }

    [Fact]
    public async Task CancellingTheFolderPickerOpensNothing()
    {
        await _app.Window.OpenThemeCommand.ExecuteAsync(null);

        Assert.False(_app.Window.IsThemeOpen);
        Assert.Equal(0, _app.AppDialogs.ManageBasesCalls);
    }

    [Fact]
    public async Task SavingWritesTheChangesToTheThemeFolder()
    {
        var window = _app.Window;
        window.OpenTheme(_themePath, _basePath);
        window.ScenesTab.SelectedScenePath = "0";
        window.ScenesTab.SceneNodeX = 40;
        await window.ScenesTab.ApplyNodeChangesCommand.ExecuteAsync(null);

        await window.SaveThemeCommand.ExecuteAsync(null);

        Assert.Empty(_app.Errors.Failures.Select(failure => $"{failure.Failure}: {failure.Exception.Message}"));
        Assert.False(window.HasUnsavedChanges);
        Assert.Contains("\"transform\":[1,0,40,0,1,-3]", File.ReadAllText(Path.Combine(_themePath, "resources", "prefab", "bg.scn")));
    }

    [Fact]
    public async Task AThemeWithValidationErrorsIsNotSaved()
    {
        // the theme registers a scene whose file does not exist
        TestApp.WriteFile(_themePath, "dependencies.json", """
            {"resources":[
              {"id":"scene","type":"Scene","path":"/prefab/bg.scn","groupId":"1"},
              {"id":"gone","type":"Scene","path":"/prefab/gone.scn","groupId":"2"}
            ],"resourceGroups":[]}
            """);
        var window = _app.Window;
        window.OpenTheme(_themePath, _basePath);
        _app.Session.MarkChanged();

        await window.SaveThemeCommand.ExecuteAsync(null);

        var (exception, failure) = Assert.Single(_app.Errors.Failures);
        Assert.Equal("Cannot save theme", failure);
        Assert.IsType<OperationFailedException>(exception);
        Assert.Contains("validation error(s) before saving", exception.Message);
        Assert.True(window.HasUnsavedChanges);
        Assert.False(Directory.Exists(Path.Combine(_app.Root, "backups")));
    }

    [Fact]
    public async Task ExportingNeedsAKnownBase()
    {
        var window = _app.Window;
        window.OpenTheme(_themePath, _basePath);
        _app.Files.Paths.Enqueue(Path.Combine(_app.Root, "out"));

        await window.ExportPackageCommand.ExecuteAsync(null);

        var (exception, _) = Assert.Single(_app.Errors.Failures);
        Assert.IsType<OperationFailedException>(exception);
        Assert.Contains("Choose the Base", exception.Message);
    }

    [Fact]
    public async Task ClosingWithUnsavedWorkCanBeCancelled()
    {
        var window = _app.Window;
        window.OpenTheme(_themePath, _basePath);
        _app.Session.MarkChanged();
        _app.Dialogs.ChoiceAnswer = DialogResult.Cancelled;

        Assert.False(await window.ConfirmCloseAsync());
    }

    [Fact]
    public async Task ClosingCanKeepTheWorkAsADraft()
    {
        var window = _app.Window;
        window.OpenTheme(_themePath, _basePath);
        _app.Session.MarkChanged();
        _app.Dialogs.ChoiceAnswer = DialogResult.Accepted;

        Assert.True(await window.ConfirmCloseAsync());
        Assert.Single(window.ListSavedDrafts());
        Assert.True(window.IsDraftSaved);
    }

    [Fact]
    public async Task ClosingWithoutChangesAsksNothing()
    {
        _app.Window.OpenTheme(_themePath, _basePath);

        Assert.True(await _app.Window.ConfirmCloseAsync());
        Assert.Empty(_app.Dialogs.Shown);
    }

    [Fact]
    public async Task GoingHomeKeepsTheThemeWhenTheUserDoesNotDiscard()
    {
        var window = _app.Window;
        window.OpenTheme(_themePath, _basePath);
        _app.Session.MarkChanged();
        _app.Dialogs.ConfirmationAnswer = false;

        await window.ReturnHomeCommand.ExecuteAsync(null);

        Assert.True(window.IsThemeOpen);
    }

    [Fact]
    public async Task GoingHomeClosesTheThemeAndEmptiesTheTabs()
    {
        var window = _app.Window;
        window.OpenTheme(_themePath, _basePath);

        await window.ReturnHomeCommand.ExecuteAsync(null);

        Assert.False(window.IsThemeOpen);
        Assert.Null(window.CurrentDraftPath);
        Assert.Empty(window.ScenesTab.SceneResources);
        Assert.Empty(window.WallpapersTab.Wallpapers);
    }
}
