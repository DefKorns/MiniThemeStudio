namespace MiniThemeStudio.Core.Themes;

public sealed record NewThemeRequest(
    string BaseThemePath,
    string DestinationPath,
    string Name,
    string Author,
    string? AtlasImagePath = null);
