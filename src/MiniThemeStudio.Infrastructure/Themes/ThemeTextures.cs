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
        (int width, int height) = ReadSize(sourceImagePath)
            ?? throw new InvalidDataException($"{Path.GetFileName(sourceImagePath)} is not a valid PNG image.");

        string definitionResourcePath = Path.ChangeExtension(resourcePath, ".json");
        string textureId = manifest.AddTexture(resourcePath, definitionResourcePath);

        string imagePath = layer.ThemeResourceFile(resourcePath);
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
        string resourcePath = $"{folder}/{name}.png";
        for (int number = 2; manifest.Resources.Any(resource => resource.Path == resourcePath) || layer.Resolve(resourcePath) is not null; number++)
        {
            resourcePath = $"{folder}/{name}_{number.ToString(CultureInfo.InvariantCulture)}.png";
        }

        return resourcePath;
    }

    // Removes an image of the theme (its PNG and its sprite sheet definition). An image the theme added also leaves
    // dependencies.json; one that replaces an image of the Base only loses the replacement, so the Base one is used
    // again. An added image a scene or an animation still draws is kept.
    public static void Delete(ThemeLayer layer, string imageResourcePath)
    {
        ArgumentNullException.ThrowIfNull(layer);
        if (layer.ResolveInTheme(imageResourcePath) is not { } imagePath)
        {
            throw new FileNotFoundException($"The theme has no image at resources{imageResourcePath}.");
        }

        string? definitionPath = layer.ResolveInTheme(Path.ChangeExtension(imageResourcePath, ".json"));
        if (layer.ResolveInBase(imageResourcePath) is null && DependencyManifest.TryLoad(layer) is { } manifest
            && manifest.Resources.FirstOrDefault(resource => resource.Type == DependencyManifest.TextureResource && resource.Path == imageResourcePath) is { } texture)
        {
            IReadOnlyList<string> users = manifest.UsersOf(texture.Id);
            if (users.Count > 0)
            {
                throw new InvalidDataException($"{Path.GetFileName(imageResourcePath)} is still drawn by {string.Join(", ", users.Select(Path.GetFileName))}.");
            }

            manifest.RemoveTexture(texture.Id);
            manifest.Save(layer);
        }

        File.Delete(imagePath);
        if (definitionPath is not null)
        {
            File.Delete(definitionPath);
        }
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

        string? definitionPath = manifest.OfType(DependencyManifest.SpriteSheet)
            .Where(sheet => sheet.References.Any(reference => reference.Id == textureId))
            .Select(sheet => sheet.Path)
            .Append(Path.ChangeExtension(imageResourcePath, ".json"))
            .OfType<string>()
            .Select(layer.Resolve)
            .FirstOrDefault(path => path is not null);
        if (definitionPath is not null
            && JsonNode.Parse(File.ReadAllText(definitionPath))?["texture"] is JsonObject texture
            && texture["width"] is JsonValue width && width.TryGetValue<int>(out int definedWidth) && definedWidth > 0
            && texture["height"] is JsonValue height && height.TryGetValue<int>(out int definedHeight) && definedHeight > 0)
        {
            return (definedWidth, definedHeight);
        }

        return layer.Resolve(imageResourcePath) is { } imagePath ? ReadSize(imagePath) : null;
    }

    private static (int Width, int Height)? ReadSize(string imagePath)
    {
        // the header is enough: the pixels are not decoded
        using SKCodec? codec = File.Exists(imagePath) ? SKCodec.Create(imagePath) : null;
        return codec is { Info: { Width: > 0, Height: > 0 } info } ? (info.Width, info.Height) : null;
    }
}
