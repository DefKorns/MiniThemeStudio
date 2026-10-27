namespace MiniThemeStudio.Core.Themes;

// a standalone PNG of the theme (not a packed atlas nor a wallpaper)
public sealed record ExtraAsset(
    string RelativePath,
    string DisplayName,
    string ImagePath,
    string? JsonPath,
    int Width,
    int Height,
    bool IsFromBaseUi = false,
    string? TextureId = null,
    bool OverridesBase = false)
{
    public IReadOnlyList<string> UsedBy { get; init; } = [];

    public string UsageText => UsedBy.Count == 0
        ? "Not used by any scene: it will not appear on the console."
        : $"Used by {string.Join(", ", UsedBy)}";
}
