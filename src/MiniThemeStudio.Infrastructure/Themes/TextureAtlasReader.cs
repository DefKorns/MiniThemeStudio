using System.Text.Json;
using MiniThemeStudio.Core.Themes;

namespace MiniThemeStudio.Infrastructure.Themes;

public sealed class TextureAtlasReader : ITextureAtlasReader
{
    private static readonly string[] DefinitionPathSegments =
    [
        "resources",
        "sprites",
        "packed.json",
    ];

    private static readonly string[] ImagePathSegments =
    [
        "resources",
        "sprites",
        "packed.png",
    ];

    public TextureAtlas? Read(string themeRootPath, string? definitionRootPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themeRootPath);

        var themeDefinitionPath = Path.Combine([themeRootPath, .. DefinitionPathSegments]);
        var definitionPath = File.Exists(themeDefinitionPath)
            ? themeDefinitionPath
            : definitionRootPath is null
                ? themeDefinitionPath
                : Path.Combine([definitionRootPath, .. DefinitionPathSegments]);
        if (!File.Exists(definitionPath))
        {
            return null;
        }

        var themeImagePath = Path.Combine([themeRootPath, .. ImagePathSegments]);
        var imagePath = File.Exists(themeImagePath)
            ? themeImagePath
            : Path.ChangeExtension(definitionPath, ".png");

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
            var pivot = ReadNumberVector(sprite, "pivot", 2);
            var sourceSize = ReadIntegerVector(sprite, "sourceSize", 2);
            var sourceOffset = ReadNumberVector(sprite, "sourceOffset", 2);

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
