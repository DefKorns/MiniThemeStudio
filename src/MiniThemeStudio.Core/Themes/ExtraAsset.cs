namespace MiniThemeStudio.Core.Themes;

public sealed record ExtraAsset(
    string RelativePath,
    string DisplayName,
    string Category,
    string ImagePath,
    string? JsonPath,
    int Width,
    int Height,
    bool IsFromBaseUi = false);
