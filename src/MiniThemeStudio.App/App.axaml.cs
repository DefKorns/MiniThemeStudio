using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MiniThemeStudio.App.Composition;
using MiniThemeStudio.App.Errors;
using MiniThemeStudio.App.Services;
using MiniThemeStudio.App.Views;
using MiniThemeStudio.Core.Settings;

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
            var services = new ServiceCollection()
                .AddMiniThemeStudio()
                .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
            desktop.Exit += (_, _) => services.Dispose();
            services.GetRequiredService<GlobalExceptionHandler>().Register();
            var logger = services.GetRequiredService<ILogger<App>>();
            var version = typeof(App).Assembly.GetName().Version?.ToString() ?? "?";
            LogStarting(logger, version);

            services.GetRequiredService<IAppearanceService>().Apply(services.GetRequiredService<IAppSettingsStore>().Current.Theme);

            var mainWindow = services.GetRequiredService<MainWindow>();
            desktop.MainWindow = mainWindow;
            Program.Instance?.ListenForActivation(() => Dispatcher.UIThread.Post(() => BringToFront(mainWindow)));
        }

        base.OnFrameworkInitializationCompleted();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Mini Theme Studio {Version} starting")]
    private static partial void LogStarting(ILogger logger, string version);

    // the app was started again: its window comes back instead of a second one
    private static void BringToFront(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Show();
        window.Activate();
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
