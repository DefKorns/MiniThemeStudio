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

    // TextureResource ids registered in dependencies.json, keyed by image path relative to resources/
    public IReadOnlyDictionary<string, string> TextureIdsByImagePath { get; init; } = new Dictionary<string, string>();

    public IReadOnlyList<string> UsageOf(string? textureId)
    {
        return textureId is not null && SceneUsageByTextureId.TryGetValue(textureId, out var usage) ? usage : [];
    }

    // the id the scenes use is the one registered for the path; the id inside the image .json may differ
    public string? TextureIdOf(string imageRelativePath, string? fallbackTextureId = null)
    {
        return TextureIdsByImagePath.TryGetValue(imageRelativePath.Replace('\\', '/').TrimStart('/'), out var id) ? id : fallbackTextureId;
    }
}
