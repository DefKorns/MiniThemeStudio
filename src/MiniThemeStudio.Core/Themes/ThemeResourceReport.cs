namespace MiniThemeStudio.Core.Themes;

public sealed record MissingTexture(
    string TextureId,
    string ImageRelativePath,
    string DefinitionRelativePath,
    IReadOnlyList<string> UsedBy);

public sealed record ThemeResourceReport(
    IReadOnlyDictionary<string, IReadOnlyList<string>> SceneUsageByTextureId,
    IReadOnlyList<MissingTexture> MissingTextures,
    IReadOnlyList<ThemeValidationIssue> Issues)
{
    public static ThemeResourceReport Empty { get; } = new(
        new Dictionary<string, IReadOnlyList<string>>(),
        [],
        []);

    public IReadOnlyList<string> UsageOf(string? textureId)
    {
        return textureId is not null && SceneUsageByTextureId.TryGetValue(textureId, out var usage) ? usage : [];
    }
}
