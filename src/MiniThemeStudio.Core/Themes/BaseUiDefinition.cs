namespace MiniThemeStudio.Core.Themes;

public sealed record BaseUiDefinition(
    string Id,
    string DisplayName,
    string LocalFolderName,
    string ConsolePath,
    string OriginalConsolePath);
