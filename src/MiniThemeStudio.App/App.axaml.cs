using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MiniThemeStudio.App.Composition;
using MiniThemeStudio.App.Errors;
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
            services.GetRequiredService<GlobalExceptionHandler>().Register(desktop);
            var logger = services.GetRequiredService<ILogger<App>>();
            var version = typeof(App).Assembly.GetName().Version?.ToString() ?? "?";
            LogStarting(logger, version);

            RequestedThemeVariant = services.GetRequiredService<IAppSettingsStore>().Current.Theme switch
            {
                ApplicationThemePreference.Light => Avalonia.Styling.ThemeVariant.Light,
                ApplicationThemePreference.Dark => Avalonia.Styling.ThemeVariant.Dark,
                _ => Avalonia.Styling.ThemeVariant.Default,
            };

            desktop.MainWindow = services.GetRequiredService<MainWindow>();
        }

        base.OnFrameworkInitializationCompleted();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Mini Theme Studio {Version} starting")]
    private static partial void LogStarting(ILogger logger, string version);

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
