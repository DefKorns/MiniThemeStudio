namespace MiniThemeStudio.Core.Settings;

public sealed record AppSettings(
    string BaseUiRootPath,
    string DraftRootPath,
    string DefaultThemeDestinationPath,
    ApplicationThemePreference Theme,
    string ConsoleHost,
    bool InitialSetupCompleted = false,
    ConsoleConnectionMode ConsoleConnectionMode = ConsoleConnectionMode.Usb);
