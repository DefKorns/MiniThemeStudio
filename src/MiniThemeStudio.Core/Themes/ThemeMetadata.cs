namespace MiniThemeStudio.Core.Themes;

public sealed record ThemeMetadata(
    string RootPath,
    string Name,
    string? Author,
    bool RequiresOverlay);
