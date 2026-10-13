namespace MiniThemeStudio.Core.Themes;

public sealed record ExtraAsset(
    string RelativePath,
    string DisplayName,
    string Category,
    string ImagePath,
    string? JsonPath,
    int Width,
    int Height,
    bool IsFromBaseUi = false,
    string? TextureId = null)
{
    public IReadOnlyList<string> UsedBy { get; init; } = [];

    public string UsageText => UsedBy.Count == 0
        ? "Not used by any scene: it will not appear on the console."
        : $"Used by {string.Join(", ", UsedBy)}";
}
