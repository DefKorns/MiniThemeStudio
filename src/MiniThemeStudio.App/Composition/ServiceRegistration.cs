using System.IO;
using Microsoft.Extensions.DependencyInjection;
using MiniThemeStudio.App.Diagnostics;
using MiniThemeStudio.App.Dialogs;
using MiniThemeStudio.App.Errors;
using MiniThemeStudio.App.Services;
using MiniThemeStudio.App.ViewModels;
using MiniThemeStudio.App.Views;
using MiniThemeStudio.Core.Audio;
using MiniThemeStudio.Core.Console;
using MiniThemeStudio.Core.Firmware;
using MiniThemeStudio.Core.Scenes;
using MiniThemeStudio.Core.Settings;
using MiniThemeStudio.Core.Themes;
using MiniThemeStudio.Infrastructure.Audio;
using MiniThemeStudio.Infrastructure.Console;
using MiniThemeStudio.Infrastructure.Firmware;
using MiniThemeStudio.Infrastructure.Settings;
using MiniThemeStudio.Infrastructure.Themes;

namespace MiniThemeStudio.App.Composition;

// The only place that picks implementations: everything else receives interfaces.
public static class ServiceRegistration
{
    public static IServiceCollection AddMiniThemeStudio(this IServiceCollection services)
    {
        services.AddLogging(builder => builder.AddFileLogging());
        services.AddSingleton<IAppSettingsStore, JsonAppSettingsStore>();

        // firmware
        services.AddSingleton<IFirmwareRegistry>(_ => FirmwareRegistry.CreateDefault(Path.Combine(
            Path.GetDirectoryName(BaseFirmwareManager.DefaultBasesRootPath)!,
            "firmwares.json")));
        services.AddSingleton<IConsoleDirectoryClientFactory, ConsoleDirectoryClientFactory>();
        services.AddSingleton<IBaseFirmwareManager, BaseFirmwareManager>();

        // themes
        services.AddSingleton<IBaseUiValidator, BaseUiValidator>();
        services.AddSingleton<IThemeProjectCreator, ThemeProjectCreator>();
        services.AddSingleton<IThemeDraftService>(provider => new ThemeDraftService(
            provider.GetRequiredService<IThemeProjectCreator>(),
            () => provider.GetRequiredService<IAppSettingsStore>().Current.DraftRootPath));
        services.AddSingleton<ISavedDraftStore>(provider => new SavedDraftStore(
            workingRootPathProvider: () => provider.GetRequiredService<IAppSettingsStore>().Current.DraftRootPath));
        services.AddSingleton<IThemeMetadataReader, ThemeMetadataReader>();
        services.AddSingleton<ITextureAtlasReader, TextureAtlasReader>();
        services.AddSingleton<ITextureAtlasEditor, TextureAtlasEditor>();
        services.AddSingleton<IAtlasLayoutMatcher, AtlasLayoutMatcher>();
        services.AddSingleton<ISpriteSheetImporter, SpriteSheetImporter>();
        services.AddSingleton<IThemeAssetManager, ThemeAssetManager>();
        services.AddSingleton<IThemeResourceIndexer, ThemeResourceIndexer>();
        services.AddSingleton<IThemeValidator, ThemeValidator>();
        services.AddSingleton<IThemeExporter, ThemeExporter>();
        services.AddSingleton<ICharacterAnimationEditor, CharacterAnimationEditor>();
        services.AddSingleton<IThemePaletteEditor, ThemePaletteEditor>();
        services.AddSingleton<ISceneEditor, SceneEditor>();

        // audio
        services.AddSingleton<IProcessRunner, DefaultProcessRunner>();
        services.AddSingleton<IFFmpegAudioConverter, FFmpegAudioConverter>();

        // user interface
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IFileDialogService, FileDialogService>();
        services.AddSingleton<IAppDialogs, AppDialogs>();
        services.AddSingleton<IAppearanceService, AppearanceService>();
        services.AddSingleton<IErrorHandler, ErrorHandler>();
        services.AddSingleton<GlobalExceptionHandler>();
        services.AddSingleton<ThemeSession>();
        services.AddSingleton<ScenesViewModel>();
        services.AddSingleton<SpritesViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();

        return services;
    }
}
