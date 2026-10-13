namespace MiniThemeStudio.Core.Themes;

public sealed record WallpaperAsset(
    string Name,
    string DisplayName,
    string ImagePath,
    string? JsonPath,
    int Width,
    int Height,
    bool IsFromBaseUi = false);
