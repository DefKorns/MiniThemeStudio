namespace MiniThemeStudio.Core.Themes;

public sealed record WallpaperAsset(
    string Name,
    string DisplayName,
    string ImagePath,
    string? JsonPath,
    int Width,
    int Height,
    bool IsFromBaseUi = false,
    string? TextureId = null)
{
    public IReadOnlyList<string> UsedBy { get; init; } = [];

    // the theme's image replaces one of the Base
    public bool OverridesBase { get; init; }

    // wall, wall_near and wall_far, which every theme lists even when it has none
    public bool IsCore { get; init; }

    public bool ExistsInTheme => !IsFromBaseUi && System.IO.File.Exists(ImagePath);

    public string UsageText => UsedBy.Count == 0
        ? "Not used by any scene: it will not appear on the console."
        : $"Used by {string.Join(", ", UsedBy)}";
}
