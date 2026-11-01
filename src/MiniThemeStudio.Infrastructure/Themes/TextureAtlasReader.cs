using System.Text.Json;
using MiniThemeStudio.Core.Themes;

namespace MiniThemeStudio.Infrastructure.Themes;

public sealed class TextureAtlasReader : ITextureAtlasReader
{
    private static readonly string[] PrimaryAtlasNames = ["packed", "nes", "hvc"];

    public TextureAtlas? Read(string themeRootPath, string? definitionRootPath = null)
    {
        IReadOnlyList<TextureAtlas> atlases = ReadAll(themeRootPath, definitionRootPath);
        return atlases.Count > 0 ? atlases[0] : null;
    }

    public IReadOnlyList<TextureAtlas> ReadAll(string themeRootPath, string? definitionRootPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themeRootPath);

        string themeSpritesPath = Path.Combine(themeRootPath, "resources", "sprites");
        string? baseSpritesPath = definitionRootPath is null ? null : Path.Combine(definitionRootPath, "resources", "sprites");
        IEnumerable<string?> names = new[] { themeSpritesPath, baseSpritesPath }
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.EnumerateFiles(directory!, "*.json"))
            .Select(Path.GetFileNameWithoutExtension)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var atlases = new List<TextureAtlas>();
        foreach (string? name in names)
        {
            string themeDefinitionPath = Path.Combine(themeSpritesPath, $"{name}.json");
            string definitionPath = File.Exists(themeDefinitionPath)
                ? themeDefinitionPath
                : Path.Combine(baseSpritesPath!, $"{name}.json");
            string themeImagePath = Path.Combine(themeSpritesPath, $"{name}.png");
            string imagePath = File.Exists(themeImagePath)
                ? themeImagePath
                : Path.ChangeExtension(definitionPath, ".png");

            if (IsPrimaryAtlasName(name!))
            {
                atlases.Add(ReadDefinition(definitionPath, imagePath));
            }
            else if (IsAtlasDefinition(definitionPath))
            {
                try
                {
                    atlases.Add(ReadDefinition(definitionPath, imagePath));
                }
                catch (InvalidDataException)
                {
                }
            }
        }

        Dictionary<string, Dictionary<string, (AtlasRectangle Frame, AtlasPoint? Pivot)>> frameOverrides = ReadDependencyFrames(themeRootPath, definitionRootPath);
        return atlases
            .Select(atlas => ApplyDependencyFrames(atlas, frameOverrides))
            .OrderBy(atlas => IsPrimaryAtlasName(atlas.Name) ? 0 : 1)
            .ThenBy(atlas => atlas.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static TextureAtlas ApplyDependencyFrames(
        TextureAtlas atlas,
        Dictionary<string, Dictionary<string, (AtlasRectangle Frame, AtlasPoint? Pivot)>> frameOverrides)
    {
        if (!frameOverrides.TryGetValue(atlas.TextureId, out Dictionary<string, (AtlasRectangle Frame, AtlasPoint? Pivot)>? overrides))
        {
            return atlas;
        }

        var sprites = atlas.Sprites
            .Select(sprite => overrides.TryGetValue(sprite.Name, out (AtlasRectangle Frame, AtlasPoint? Pivot) value)
                ? sprite with { Frame = value.Frame, Pivot = value.Pivot ?? sprite.Pivot }
                : sprite)
            .ToList();
        var known = atlas.Sprites.Select(sprite => sprite.Name).ToHashSet(StringComparer.Ordinal);
        sprites.AddRange(overrides
            .Where(entry => !known.Contains(entry.Key))
            .Select(entry => new SpriteRegion(
                entry.Key,
                entry.Value.Frame,
                entry.Value.Pivot ?? new AtlasPoint(entry.Value.Frame.Width / 2.0, entry.Value.Frame.Height / 2.0),
                new AtlasSize(entry.Value.Frame.Width, entry.Value.Frame.Height),
                new AtlasPoint(0, 0))));

        return atlas with { Sprites = sprites };
    }

    private static Dictionary<string, Dictionary<string, (AtlasRectangle Frame, AtlasPoint? Pivot)>> ReadDependencyFrames(
        string themeRootPath,
        string? definitionRootPath)
    {
        var result = new Dictionary<string, Dictionary<string, (AtlasRectangle, AtlasPoint?)>>(StringComparer.Ordinal);
        try
        {
            // SpriteResource entries carry their frame inline, as JSON text in "content"
            IEnumerable<ManifestResource> spriteResources = DependencyManifest.TryLoad(new ThemeLayer(themeRootPath, definitionRootPath))?.OfType("SpriteResource") ?? [];
            foreach (ManifestResource resource in spriteResources)
            {
                if (resource.Content is not { } contentText)
                {
                    continue;
                }

                using var content = JsonDocument.Parse(contentText);
                string textureId = content.RootElement.GetProperty("textureId").GetString() ?? "";
                string id = resource.Id;
                if (!id.StartsWith(textureId + ".", StringComparison.Ordinal))
                {
                    continue;
                }

                int[] frame = ReadIntegerVector(content.RootElement, "textureFrame", 4);
                AtlasPoint? pivot = content.RootElement.TryGetProperty("pivot", out _)
                    ? ReadNumberVector(content.RootElement, "pivot", 2) is var values ? new AtlasPoint(values[0], values[1]) : null
                    : null;
                if (!result.TryGetValue(textureId, out Dictionary<string, (AtlasRectangle, AtlasPoint?)>? sprites))
                {
                    sprites = new Dictionary<string, (AtlasRectangle, AtlasPoint?)>(StringComparer.Ordinal);
                    result[textureId] = sprites;
                }

                sprites[id[(textureId.Length + 1)..]] = (new AtlasRectangle(frame[0], frame[1], frame[2], frame[3]), pivot);
            }
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or InvalidDataException)
        {
            return [];
        }

        return result;
    }

    public static bool IsAtlasDefinition(string definitionPath)
    {
        if (IsPrimaryAtlasName(Path.GetFileNameWithoutExtension(definitionPath)))
        {
            return true;
        }

        try
        {
            using FileStream stream = File.OpenRead(definitionPath);
            using var document = JsonDocument.Parse(stream);
            return document.RootElement.TryGetProperty("sprites", out JsonElement sprites)
                && sprites.ValueKind == JsonValueKind.Object
                && sprites.EnumerateObject().Skip(1).Any();
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            return false;
        }
    }

    public static bool IsPrimaryAtlasName(string name)
    {
        return PrimaryAtlasNames.Contains(name, StringComparer.OrdinalIgnoreCase);
    }

    private static TextureAtlas ReadDefinition(string definitionPath, string imagePath)
    {
        try
        {
            using FileStream stream = File.OpenRead(definitionPath);
            using var document = JsonDocument.Parse(stream);
            JsonElement root = document.RootElement;
            JsonElement texture = root.GetProperty("texture");
            List<SpriteRegion> sprites = ReadSprites(root.GetProperty("sprites"));

            return new TextureAtlas(
                definitionPath,
                imagePath,
                texture.GetProperty("id").GetString() ?? string.Empty,
                new AtlasSize(
                    texture.GetProperty("width").GetInt32(),
                    texture.GetProperty("height").GetInt32()),
                sprites);
        }
        catch (Exception exception) when (
            exception is JsonException
                or InvalidOperationException
                or KeyNotFoundException
                or FormatException
                or OverflowException)
        {
            throw new InvalidDataException($"Invalid texture atlas: {definitionPath}", exception);
        }
    }

    private static List<SpriteRegion> ReadSprites(JsonElement spritesElement)
    {
        var sprites = new List<SpriteRegion>();

        foreach (JsonProperty spriteProperty in spritesElement.EnumerateObject())
        {
            JsonElement sprite = spriteProperty.Value;
            int[] frame = ReadIntegerVector(sprite, "textureFrame", 4);
            double[] pivot = sprite.TryGetProperty("pivot", out _)
                ? ReadNumberVector(sprite, "pivot", 2)
                : [frame[2] / 2.0, frame[3] / 2.0];
            int[] sourceSize = sprite.TryGetProperty("sourceSize", out _)
                ? ReadIntegerVector(sprite, "sourceSize", 2)
                : [frame[2], frame[3]];
            double[] sourceOffset = sprite.TryGetProperty("sourceOffset", out _)
                ? ReadNumberVector(sprite, "sourceOffset", 2)
                : [0.0, 0.0];

            sprites.Add(new SpriteRegion(
                spriteProperty.Name,
                new AtlasRectangle(frame[0], frame[1], frame[2], frame[3]),
                new AtlasPoint(pivot[0], pivot[1]),
                new AtlasSize(sourceSize[0], sourceSize[1]),
                new AtlasPoint(sourceOffset[0], sourceOffset[1])));
        }

        return sprites;
    }

    private static int[] ReadIntegerVector(
        JsonElement owner,
        string propertyName,
        int expectedLength)
    {
        int[] values = owner.GetProperty(propertyName)
            .EnumerateArray()
            .Select(value => value.GetInt32())
            .ToArray();

        if (values.Length != expectedLength)
        {
            throw new InvalidDataException(
                $"Property '{propertyName}' must contain {expectedLength} values.");
        }

        return values;
    }

    private static double[] ReadNumberVector(
        JsonElement owner,
        string propertyName,
        int expectedLength)
    {
        double[] values = owner.GetProperty(propertyName)
            .EnumerateArray()
            .Select(value => value.GetDouble())
            .ToArray();

        if (values.Length != expectedLength)
        {
            throw new InvalidDataException(
                $"Property '{propertyName}' must contain {expectedLength} values.");
        }

        return values;
    }
}
