using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Markup.Xaml;
using MiniThemeStudio.App.ViewModels;
using MiniThemeStudio.App.Views;
using MiniThemeStudio.Core.Settings;
using MiniThemeStudio.Infrastructure.Console;
using MiniThemeStudio.Infrastructure.Firmware;
using MiniThemeStudio.Infrastructure.Settings;
using MiniThemeStudio.Infrastructure.Themes;

namespace MiniThemeStudio.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            DisableAvaloniaDataAnnotationValidation();
            var settingsStore = new JsonAppSettingsStore();
            RequestedThemeVariant = settingsStore.Current.Theme switch
            {
                ApplicationThemePreference.Light => Avalonia.Styling.ThemeVariant.Light,
                ApplicationThemePreference.Dark => Avalonia.Styling.ThemeVariant.Dark,
                _ => Avalonia.Styling.ThemeVariant.Default,
            };

            var projectCreator = new ThemeProjectCreator();
            var firmwareRegistry = new FirmwareRegistry();
            var baseFirmwareManager = new BaseFirmwareManager(
                firmwareRegistry,
                new BaseUiValidator(),
                settingsStore.Current.BaseUiRootPath);
            var themeAssetManager = new ThemeAssetManager();
            var themeValidator = new ThemeValidator();

            desktop.MainWindow = new MainWindow(
                settingsStore,
                new ConsoleBaseUiImporter(
                    new BaseUiValidator(),
                    new ConsoleDirectoryClientFactory()),
                new BaseUiValidator(),
                baseFirmwareManager,
                themeAssetManager,
                themeValidator)
            {
                DataContext = new MainWindowViewModel(
                    new ThemeMetadataReader(),
                    new TextureAtlasReader(),
                    new ThemeDraftService(
                        projectCreator,
                        () => settingsStore.Current.DraftRootPath),
                    new TextureAtlasEditor(),
                    themeAssetManager,
                    themeValidator,
                    baseFirmwareManager),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void DisableAvaloniaDataAnnotationValidation()
    {
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}
