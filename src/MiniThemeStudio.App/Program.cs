using System;
using Avalonia;
using MiniThemeStudio.App.Services;

namespace MiniThemeStudio.App;

internal sealed class Program
{
    // the running instance, for App to bring its window forward when the app is started again
    internal static SingleInstance? Instance { get; private set; }

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        using var instance = SingleInstance.Acquire();
        if (!instance.IsFirst)
        {
            instance.ActivateFirst();
            return;
        }

        Instance = instance;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
