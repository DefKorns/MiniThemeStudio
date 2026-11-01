using Microsoft.Extensions.DependencyInjection;
using MiniThemeStudio.App.Tests.TestDoubles;
using MiniThemeStudio.App.ViewModels;
using MiniThemeStudio.Core.Settings;

namespace MiniThemeStudio.App.Tests.ViewModels;

public sealed class EditingModeTests : IDisposable
{
    private readonly TestApp _app = new();

    public void Dispose() => _app.Dispose();

    [Fact]
    public void ThemesStartInSimpleEditingAndTheChoiceIsSaved()
    {
        EditingMode mode = _app.Window.Mode;
        Assert.True(mode.IsSimple);

        mode.IsAdvanced = true;

        Assert.True(_app.Services.GetRequiredService<IAppSettingsStore>().Current.AdvancedEditing);
        Assert.False(mode.IsSimple);
    }

    [Fact]
    public void LeavingAdvancedEditingLeavesTheTabsItHides()
    {
        _app.Window.OpenTheme(_app.CreateTheme(), _app.CreateBase());
        _app.Window.Mode.IsAdvanced = true;
        _app.Window.SelectedTabIndex = 2;

        _app.Window.Mode.IsAdvanced = false;

        Assert.Equal(0, _app.Window.SelectedTabIndex);
    }
}
