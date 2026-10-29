using Avalonia;
using Avalonia.Headless;

namespace MiniThemeStudio.App.Tests.TestDoubles;

// Avalonia without windows or a screen, drawing with Skia, so view models can build their bitmap previews.
// It can be set up once per process, so every test shares it.
internal static class HeadlessAvalonia
{
    private static readonly Lazy<bool> Platform = new(() =>
    {
        AppBuilder.Configure<Application>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();
        return true;
    });

    public static void EnsureStarted() => _ = Platform.Value;
}
