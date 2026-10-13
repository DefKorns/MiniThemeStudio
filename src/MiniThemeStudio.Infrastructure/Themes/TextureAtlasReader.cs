using System.Text.Json;
using MiniThemeStudio.Core.Themes;

namespace MiniThemeStudio.Infrastructure.Themes;

public sealed class TextureAtlasReader : ITextureAtlasReader
{
    private static readonly string[] PrimaryAtlasNames = ["packed", "nes", "hvc"];

    public TextureAtlas? Read(string themeRootPath, string? definitionRootPath = null)
    {
        var atlases = ReadAll(themeRootPath, definitionRootPath);
        return atlases.Count > 0 ? atlases[0] : null;
    }

    public IReadOnlyList<TextureAtlas> ReadAll(string themeRootPath, string? definitionRootPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themeRootPath);

        var themeSpritesPath = Path.Combine(themeRootPath, "resources", "sprites");
        var baseSpritesPath = definitionRootPath is null ? null : Path.Combine(definitionRootPath, "resources", "sprites");
        var names = new[] { themeSpritesPath, baseSpritesPath }
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.EnumerateFiles(directory!, "*.json"))
            .Select(Path.GetFileNameWithoutExtension)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var atlases = new List<TextureAtlas>();
        foreach (var name in names)
        {
            var themeDefinitionPath = Path.Combine(themeSpritesPath, $"{name}.json");
            var definitionPath = File.Exists(themeDefinitionPath)
                ? themeDefinitionPath
                : Path.Combine(baseSpritesPath!, $"{name}.json");
            var themeImagePath = Path.Combine(themeSpritesPath, $"{name}.png");
            var imagePath = File.Exists(themeImagePath)
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

        return atlases
            .OrderBy(atlas => IsPrimaryAtlasName(atlas.Name) ? 0 : 1)
            .ThenBy(atlas => atlas.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static bool IsAtlasDefinition(string definitionPath)
    {
        if (IsPrimaryAtlasName(Path.GetFileNameWithoutExtension(definitionPath)))
        {
            return true;
        }

        try
        {
            using var stream = File.OpenRead(definitionPath);
            using var document = JsonDocument.Parse(stream);
            return document.RootElement.TryGetProperty("sprites", out var sprites)
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
            using var stream = File.OpenRead(definitionPath);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            var texture = root.GetProperty("texture");
            var sprites = ReadSprites(root.GetProperty("sprites"));

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

        foreach (var spriteProperty in spritesElement.EnumerateObject())
        {
            var sprite = spriteProperty.Value;
            var frame = ReadIntegerVector(sprite, "textureFrame", 4);
            var pivot = sprite.TryGetProperty("pivot", out _)
                ? ReadNumberVector(sprite, "pivot", 2)
                : [frame[2] / 2.0, frame[3] / 2.0];
            var sourceSize = sprite.TryGetProperty("sourceSize", out _)
                ? ReadIntegerVector(sprite, "sourceSize", 2)
                : [frame[2], frame[3]];
            var sourceOffset = sprite.TryGetProperty("sourceOffset", out _)
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
        var values = owner.GetProperty(propertyName)
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
        var values = owner.GetProperty(propertyName)
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
