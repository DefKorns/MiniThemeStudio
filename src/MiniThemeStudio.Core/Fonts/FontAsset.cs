namespace MiniThemeStudio.Core.Fonts;

public sealed record FontAsset(
    string Name,
    string DisplayName,
    string FilePath,
    string RelativePath,
    int GlyphCount,
    int TextureWidth,
    int TextureHeight,
    int LineHeight,
    bool IsFromBaseUi = false)
{
    public IReadOnlyList<string> Roles { get; init; } = [];

    public string RolesText => Roles.Count == 0
        ? "UI Font"
        : string.Join(", ", Roles);
}
