using Microsoft.Extensions.DependencyInjection;
using MiniThemeStudio.App.Composition;
using MiniThemeStudio.App.Dialogs;
using MiniThemeStudio.App.Errors;
using MiniThemeStudio.App.Services;
using MiniThemeStudio.App.ViewModels;
using MiniThemeStudio.Core.Firmware;
using MiniThemeStudio.Core.Settings;
using MiniThemeStudio.Core.Themes;
using MiniThemeStudio.Infrastructure.Firmware;
using MiniThemeStudio.Infrastructure.Settings;
using MiniThemeStudio.Infrastructure.Themes;

namespace MiniThemeStudio.App.Tests.TestDoubles;

// The app as composed in production, with the UI services replaced by test doubles and every folder
// (settings, Bases, drafts, backups) inside a temporary directory, so tests never touch the user's data.
public sealed class TestApp : IDisposable
{
    // a valid 1x1 PNG
    public static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    public TestApp()
    {
        try
        {
            var settings = new JsonAppSettingsStore(Path.Combine(Root, "settings.json"));
            settings.Save(new AppSettings(
                DraftRootPath: Path.Combine(Root, "drafts"),
                DefaultThemeDestinationPath: Path.Combine(Root, "themes"),
                Theme: ApplicationThemePreference.System,
                ConsoleHost: "169.254.13.37",
                InitialSetupCompleted: true));

            var services = new ServiceCollection().AddMiniThemeStudio();
            services.AddSingleton<IAppSettingsStore>(settings);
            services.AddSingleton<IFirmwareRegistry>(_ => FirmwareRegistry.CreateDefault());
            services.AddSingleton<IBaseFirmwareManager>(provider =>
                new BaseFirmwareManager(provider.GetRequiredService<IFirmwareRegistry>(), basesRootPath: Path.Combine(Root, "bases")));
            services.AddSingleton<IThemeDraftService>(provider => new ThemeDraftService(
                provider.GetRequiredService<IThemeProjectCreator>(),
                () => Path.Combine(Root, "drafts"),
                Path.Combine(Root, "backups")));
            services.AddSingleton<ISavedDraftStore>(_ => new SavedDraftStore(Path.Combine(Root, "saved-drafts"), () => Path.Combine(Root, "drafts")));
            services.AddSingleton<IDialogService>(Dialogs);
            services.AddSingleton<IFileDialogService>(Files);
            services.AddSingleton<IAppDialogs>(AppDialogs);
            services.AddSingleton<IAppearanceService>(Appearance);
            services.AddSingleton<IErrorHandler>(Errors);
            Services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        }
        catch
        {
            // a failed start leaves no temporary folder behind
            Directory.Delete(Root, true);
            throw;
        }
    }

    public string Root { get; } = Directory.CreateTempSubdirectory("mts-app-tests-").FullName;

    public FakeDialogService Dialogs { get; } = new();

    public FakeFileDialogService Files { get; } = new();

    public FakeAppDialogs AppDialogs { get; } = new();

    public FakeAppearanceService Appearance { get; } = new();

    public RecordingErrorHandler Errors { get; } = new();

    public ServiceProvider Services { get; }

    public MainWindowViewModel Window => Services.GetRequiredService<MainWindowViewModel>();

    public ThemeSession Session => Services.GetRequiredService<ThemeSession>();

    // a Base with one scene (bg.scn) drawing one texture (wall.png), and the sprite atlas every Base has
    public string CreateBase()
    {
        var basePath = Path.Combine(Root, "base");
        WriteFile(basePath, "dependencies.json", """
            {"resources":[
              {"id":"scene","type":"Scene","dependencies":[{"id":"wall","refCount":1}],"path":"/prefab/bg.scn","groupId":"1"},
              {"id":"wall","type":"TextureResource","path":"/layout/asset/sprite/wallpaper/wall.png","groupId":"2"}
            ],"resourceGroups":[]}
            """);
        WriteFile(basePath, "resources/prefab/bg.scn", """
            {"rootWorldNode":{"id":"r","name":"BG","transform":[1,0,0,0,1,0],"components":[],"children":[
              {"id":"a","name":"wall","transform":[1,0,0,0,1,-3],"components":[{"type":"TextureComponent","textureId":"wall","size":[1,1],"pivot":[0.5,0.5]}],"children":[]}
            ]}}
            """);
        WriteBytes(basePath, "resources/layout/asset/sprite/wallpaper/wall.png", Png);
        WriteFile(basePath, "resources/layout/asset/sprite/wallpaper/wall.json", """{"texture":{"id":"wall","width":1,"height":1},"sprites":{}}""");
        WriteBytes(basePath, "resources/sprites/packed.png", Png);
        WriteFile(basePath, "resources/sprites/packed.json", """{"texture":{"id":"packed","width":1,"height":1},"sprites":{"dot.png":{"textureFrame":[0,0,1,1]}}}""");
        return basePath;
    }

    // a theme that changes nothing of its Base
    public string CreateTheme()
    {
        var themePath = Path.Combine(Root, "themes", "my_theme");
        WriteFile(themePath, "author.txt", "Tester");
        return themePath;
    }

    public static void WriteFile(string root, string relativePath, string content)
    {
        var path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    public static void WriteBytes(string root, string relativePath, byte[] content)
    {
        var path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
    }

    public void Dispose()
    {
        Services.Dispose();
        Directory.Delete(Root, true);
    }
}
