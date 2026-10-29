using System.Text.Json;
using MiniThemeStudio.Core.Themes;

namespace MiniThemeStudio.Infrastructure.Themes;

public sealed class ThemeResourceIndexer : IThemeResourceIndexer
{
    public ThemeResourceReport Analyze(string themePath, string? baseUiPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themePath);

        var issues = new List<ThemeValidationIssue>();
        var resolver = new ThemeLayer(themePath, baseUiPath);
        List<ManifestResource> resources;
        try
        {
            if (DependencyManifest.TryLoad(resolver) is not { } manifest)
            {
                issues.Add(new ThemeValidationIssue("dependencies.json not found: scene texture references were not checked.", false));
                return ThemeResourceReport.Empty with { Issues = issues };
            }

            resources = manifest.Resources.ToList();
        }
        catch (InvalidDataException exception)
        {
            issues.Add(new ThemeValidationIssue($"dependencies.json could not be read: {exception.Message}", true));
            return ThemeResourceReport.Empty with { Issues = issues };
        }

        var resourcesById = resources.GroupBy(resource => resource.Id).ToDictionary(group => group.Key, group => group.First());
        var sheetsByTextureId = resources
            .Where(resource => resource.Type == DependencyManifest.SpriteSheet && resource.Path is not null && resource.References.Count > 0)
            .GroupBy(resource => resource.References[0].Id)
            .ToDictionary(group => group.Key, group => group.First().Path!);

        var references = new List<TextureReference>();
        foreach (ManifestResource? scene in resources.Where(resource => resource.Type is DependencyManifest.Scene or DependencyManifest.SceneAnimation && resource.Path is not null))
        {
            string? scenePath = resolver.Resolve(scene.Path!);
            if (scenePath is null)
            {
                issues.Add(new ThemeValidationIssue($"Scene file missing: resources{scene.Path}", true));
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(scenePath));
                CollectReferences(document.RootElement, Path.GetFileName(scene.Path!), references);
            }
            catch (JsonException exception)
            {
                issues.Add(new ThemeValidationIssue($"Scene {scene.Path} is not strict JSON and was not analysed: {exception.Message}", false));
            }
        }

        var usage = references
            .GroupBy(reference => reference.TextureId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group.Select(reference => reference.Scene).Distinct().Order(StringComparer.Ordinal).ToList());

        foreach (string? unregistered in usage.Keys.Where(id => !resourcesById.ContainsKey(id)).Order(StringComparer.Ordinal))
        {
            issues.Add(new ThemeValidationIssue(
                $"Texture {unregistered} used by {string.Join(", ", usage[unregistered])} is not registered in dependencies.json.",
                true));
        }

        var missing = new List<MissingTexture>();
        foreach (ManifestResource? texture in resources.Where(resource => resource.Type == DependencyManifest.TextureResource && resource.Path is not null))
        {
            if (resolver.Resolve(texture.Path!) is not null)
            {
                continue;
            }

            IReadOnlyList<string> usedBy = usage.TryGetValue(texture.Id, out IReadOnlyList<string>? scenes) ? scenes : [];
            string definitionPath = sheetsByTextureId.TryGetValue(texture.Id, out string? sheetPath)
                ? sheetPath
                : Path.ChangeExtension(texture.Path!, ".json");
            missing.Add(new MissingTexture(texture.Id, ToRelative(texture.Path!), ToRelative(definitionPath), usedBy));
            issues.Add(new ThemeValidationIssue(
                $"Texture image missing: resources{texture.Path}" + (usedBy.Count > 0 ? $" (used by {string.Join(", ", usedBy)})" : ""),
                usedBy.Count > 0));
        }

        CheckNodeSizes(references, sheetsByTextureId, resolver, issues);

        var textureIdsByImagePath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (ManifestResource? texture in resources.Where(resource => resource.Type == DependencyManifest.TextureResource && resource.Path is not null))
        {
            textureIdsByImagePath.TryAdd(texture.Path!.TrimStart('/'), texture.Id);
        }

        return new ThemeResourceReport(usage, missing, issues) { TextureIdsByImagePath = textureIdsByImagePath };
    }

    private static void CheckNodeSizes(
        List<TextureReference> references,
        Dictionary<string, string> sheetsByTextureId,
        ThemeLayer resolver,
        List<ThemeValidationIssue> issues)
    {
        foreach (TextureReference? reference in references.Where(candidate => candidate.NodeSize is not null).DistinctBy(candidate => (candidate.TextureId, candidate.Scene, candidate.NodeSize)))
        {
            if (!sheetsByTextureId.TryGetValue(reference.TextureId, out string? sheetPath)
                || resolver.ResolveInTheme(sheetPath) is not { } themeSheet
                || ReadTextureSize(themeSheet) is not { } themeSize)
            {
                continue;
            }

            if (resolver.ResolveInBase(sheetPath) is { } baseSheet
                && ReadTextureSize(baseSheet) is { } baseSize
                && baseSize != themeSize
                && IsUniformScale(baseSize, reference.NodeSize!.Value)
                && !IsUniformScale(themeSize, reference.NodeSize.Value))
            {
                issues.Add(new ThemeValidationIssue(
                    $"{Path.GetFileName(sheetPath)} is {themeSize.Width}x{themeSize.Height} instead of the original {baseSize.Width}x{baseSize.Height}, "
                    + $"but {reference.Scene} still draws it at {reference.NodeSize!.Value.Width}x{reference.NodeSize.Value.Height}; it may look stretched.",
                    false));
            }
        }
    }

    private static bool IsUniformScale((int Width, int Height) texture, (int Width, int Height) node)
    {
        if (texture.Width <= 0 || texture.Height <= 0 || node.Width <= 0 || node.Height <= 0)
        {
            return false;
        }

        double horizontal = (double)node.Width / texture.Width;
        double vertical = (double)node.Height / texture.Height;
        return Math.Abs(horizontal - vertical) <= 0.02 * Math.Max(horizontal, vertical);
    }

    private static void CollectReferences(JsonElement element, string scene, List<TextureReference> references)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (IsString(element, "type", "TextureComponent")
                    && element.TryGetProperty("textureId", out JsonElement textureId)
                    && textureId.ValueKind == JsonValueKind.String
                    && textureId.GetString() is { Length: > 0 } id)
                {
                    references.Add(new TextureReference(id, scene, ReadSize(element)));
                }

                if (IsString(element, "componentType", "TextureComponent")
                    && IsString(element, "propertyId", "Texture")
                    && element.TryGetProperty("keyCurves", out JsonElement curves)
                    && curves.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement key in curves.EnumerateArray())
                    {
                        if (key.TryGetProperty("value", out JsonElement value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } animatedId)
                        {
                            references.Add(new TextureReference(animatedId, scene, null));
                        }
                    }
                }

                foreach (JsonProperty property in element.EnumerateObject())
                {
                    CollectReferences(property.Value, scene, references);
                }

                break;
            case JsonValueKind.Array:
                foreach (JsonElement item in element.EnumerateArray())
                {
                    CollectReferences(item, scene, references);
                }

                break;
        }
    }

    private static bool IsString(JsonElement element, string propertyName, string expected)
    {
        return element.TryGetProperty(propertyName, out JsonElement value)
            && value.ValueKind == JsonValueKind.String
            && value.GetString() == expected;
    }

    private static (int Width, int Height)? ReadSize(JsonElement component)
    {
        if (component.TryGetProperty("size", out JsonElement size) && size.ValueKind == JsonValueKind.Array && size.GetArrayLength() == 2)
        {
            return ((int)Math.Round(size[0].GetDouble()), (int)Math.Round(size[1].GetDouble()));
        }

        return null;
    }

    private static (int Width, int Height)? ReadTextureSize(string sheetPath)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(sheetPath));
            JsonElement texture = document.RootElement.GetProperty("texture");
            return (texture.GetProperty("width").GetInt32(), texture.GetProperty("height").GetInt32());
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    private static string ToRelative(string resourcePath)
    {
        return Path.Combine("resources", resourcePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
    }

    private sealed record TextureReference(string TextureId, string Scene, (int Width, int Height)? NodeSize);
}
