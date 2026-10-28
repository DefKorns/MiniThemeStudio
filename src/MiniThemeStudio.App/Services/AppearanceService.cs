using Avalonia;
using Avalonia.Styling;
using MiniThemeStudio.Core.Settings;

namespace MiniThemeStudio.App.Services;

public interface IAppearanceService
{
    void Apply(ApplicationThemePreference preference);
}

public sealed class AppearanceService : IAppearanceService
{
    public void Apply(ApplicationThemePreference preference)
    {
        if (Application.Current is { } application)
        {
            application.RequestedThemeVariant = preference switch
            {
                ApplicationThemePreference.Light => ThemeVariant.Light,
                ApplicationThemePreference.Dark => ThemeVariant.Dark,
                _ => ThemeVariant.Default,
            };
        }
    }
}
