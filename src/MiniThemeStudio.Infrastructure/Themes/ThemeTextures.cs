using System.Globalization;
using System.Text.Json.Nodes;
using SkiaSharp;

namespace MiniThemeStudio.Infrastructure.Themes;

// textures the editor adds to a theme: the PNG, its sprite sheet definition (an empty one for a plain image)
// and their entries in dependencies.json
internal static class ThemeTextures
{
    // copies the PNG to resourcePath (e.g. /layout/asset/sprite/scene/logo.png) and returns the new texture id;
    // the manifest is changed but not saved, so the caller saves it with its own changes
    public static string Register(ThemeLayer layer, DependencyManifest manifest, string resourcePath, string sourceImagePath)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(manifest);
        var (width, height) = ReadSize(sourceImagePath)
            ?? throw new InvalidDataException($"{Path.GetFileName(sourceImagePath)} is not a valid PNG image.");

        var definitionResourcePath = Path.ChangeExtension(resourcePath, ".json");
        var textureId = manifest.AddTexture(resourcePath, definitionResourcePath);

        var imagePath = layer.ThemeResourceFile(resourcePath);
        Directory.CreateDirectory(Path.GetDirectoryName(imagePath)!);
        File.Copy(sourceImagePath, imagePath, false);
        layer.WriteThemeResource(
            definitionResourcePath,
            EngineJson.ToCompactString(new JsonObject
            {
                ["texture"] = new JsonObject { ["id"] = textureId, ["width"] = width, ["height"] = height },
                ["sprites"] = new JsonObject(),
            }));
        return textureId;
    }

    // folder/name.png, or name_2.png, name_3.png... when the theme, its Base or the manifest already has it
    public static string FreeResourcePath(ThemeLayer layer, DependencyManifest manifest, string folder, string name)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(manifest);
        var resourcePath = $"{folder}/{name}.png";
        for (var number = 2; manifest.Resources.Any(resource => resource.Path == resourcePath) || layer.Resolve(resourcePath) is not null; number++)
        {
            resourcePath = $"{folder}/{name}_{number.ToString(CultureInfo.InvariantCulture)}.png";
        }

        return resourcePath;
    }

    // the pixel size of a registered texture: from its sprite sheet definition, or from the image itself
    public static (int Width, int Height)? Size(ThemeLayer layer, DependencyManifest manifest, string textureId)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.FindById(textureId) is not { Path: { } imageResourcePath })
        {
            return null;
        }

        var definitionPath = manifest.OfType(DependencyManifest.SpriteSheet)
            .Where(sheet => sheet.References.Any(reference => reference.Id == textureId))
            .Select(sheet => sheet.Path)
            .Append(Path.ChangeExtension(imageResourcePath, ".json"))
            .OfType<string>()
            .Select(layer.Resolve)
            .FirstOrDefault(path => path is not null);
        if (definitionPath is not null
            && JsonNode.Parse(File.ReadAllText(definitionPath))?["texture"] is JsonObject texture
            && texture["width"] is JsonValue width && width.TryGetValue<int>(out var definedWidth) && definedWidth > 0
            && texture["height"] is JsonValue height && height.TryGetValue<int>(out var definedHeight) && definedHeight > 0)
        {
            return (definedWidth, definedHeight);
        }

        return layer.Resolve(imageResourcePath) is { } imagePath ? ReadSize(imagePath) : null;
    }

    private static (int Width, int Height)? ReadSize(string imagePath)
    {
        // the header is enough: the pixels are not decoded
        using var codec = File.Exists(imagePath) ? SKCodec.Create(imagePath) : null;
        return codec is { Info: { Width: > 0, Height: > 0 } info } ? (info.Width, info.Height) : null;
    }
}
