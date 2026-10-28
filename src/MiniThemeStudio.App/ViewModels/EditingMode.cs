using CommunityToolkit.Mvvm.ComponentModel;
using MiniThemeStudio.Core.Settings;

namespace MiniThemeStudio.App.ViewModels;

// Simple editing shows what most themes change: the atlas, the wallpapers, the character frames, the fonts and
// the music. Advanced editing adds the scenes, the colours, the scrolling layers and the character animations.
// It is an app setting, so the app opens the next theme in the same mode.
public sealed class EditingMode : ObservableObject
{
    private readonly IAppSettingsStore _settingsStore;

    public EditingMode(IAppSettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
    }

    public bool IsAdvanced
    {
        get => _settingsStore.Current.AdvancedEditing;
        set
        {
            if (value == IsAdvanced)
            {
                return;
            }

            _settingsStore.Save(_settingsStore.Current with { AdvancedEditing = value });
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSimple));
        }
    }

    public bool IsSimple => !IsAdvanced;
}
