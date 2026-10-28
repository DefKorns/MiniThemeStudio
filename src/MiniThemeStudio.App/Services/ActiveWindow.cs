using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace MiniThemeStudio.App.Services;

// The window that owns a dialog or picker: the focused one (a dialog may be open over the main window),
// otherwise the main window. Lets view models ask for dialogs without knowing about windows.
internal static class ActiveWindow
{
    public static Window Get()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            throw new InvalidOperationException("Dialogs need the desktop application lifetime.");
        }

        return desktop.Windows.LastOrDefault(window => window.IsActive)
            ?? desktop.MainWindow
            ?? throw new InvalidOperationException("There is no window to own the dialog.");
    }
}
