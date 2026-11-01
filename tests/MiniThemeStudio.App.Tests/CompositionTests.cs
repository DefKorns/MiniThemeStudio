using MiniThemeStudio.App.Tests.TestDoubles;
using MiniThemeStudio.App.ViewModels;

namespace MiniThemeStudio.App.Tests;

public sealed class CompositionTests : IDisposable
{
    private readonly TestApp _app = new();

    public void Dispose() => _app.Dispose();

    [Fact]
    public void TheServiceContainerBuildsTheWindowAndItsTabs()
    {
        MainWindowViewModel window = _app.Window;

        Assert.NotNull(window.ScenesTab);
        Assert.NotNull(window.SpritesTab);
        Assert.NotNull(window.WallpapersTab);
        Assert.NotNull(window.CharactersTab);
        Assert.NotNull(window.FontsTab);
        Assert.NotNull(window.ColorsTab);
        Assert.NotNull(window.AudioTab);
        Assert.NotNull(window.ValidationTab);
        Assert.False(window.IsThemeOpen);
    }
}
