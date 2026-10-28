using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using MiniThemeStudio.Core.Themes;
using SkiaSharp;

namespace MiniThemeStudio.Infrastructure.Themes;

public sealed class CharacterAnimationEditor : ICharacterAnimationEditor
{
    private const string LayoutPrefix = "/layout/lyt_";

    public IReadOnlyList<string> GetCharacters(string themePath, string? baseUiPath)
    {
        return ReadDependencies(themePath, baseUiPath)
            .Where(resource => resource.Type == DependencyManifest.SceneAnimation && resource.Path?.StartsWith(LayoutPrefix, StringComparison.Ordinal) == true)
            .Select(resource => CharacterOf(resource.Path!))
            .Where(character => character.Length > 0 && GetNodes(themePath, baseUiPath, character).Count > 0)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    public IReadOnlyList<CharacterAnimation> GetAnimations(string themePath, string? baseUiPath, string character)
    {
        var layer = new ThemeLayer(themePath, baseUiPath);
        var animations = new List<CharacterAnimation>();
        foreach (ManifestResource? resource in ReadDependencies(themePath, baseUiPath)
            .Where(resource => resource.Type == DependencyManifest.SceneAnimation && resource.Path is not null && CharacterOf(resource.Path) == character)
            .OrderBy(resource => resource.Path, StringComparer.Ordinal))
        {
            string? path = layer.Resolve(resource.Path!);
            if (path is null)
            {
                continue;
            }

            JsonObject root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            var tracks = TextureTracks(root)
                .Select(track => new AnimationTrack(
                    NodeName(track),
                    track["keyCurves"]!.AsArray()
                        .Select(key => new AnimationKey(key!["time"]!.GetValue<double>(), key["value"]!.GetValue<string>()))
                        .ToList()))
                .ToList();
            if (tracks.Count == 0)
            {
                continue;
            }

            string fileName = Path.GetFileNameWithoutExtension(resource.Path!);
            string name = fileName.Contains('@') ? fileName[(fileName.IndexOf('@') + 1)..] : "main";
            animations.Add(new CharacterAnimation(resource.Path!, name, root["duration"]?.GetValue<double>() ?? 0, tracks));
        }

        return animations;
    }

    public IReadOnlyList<CharacterNode> GetNodes(string themePath, string? baseUiPath, string character)
    {
        var layer = new ThemeLayer(themePath, baseUiPath);
        var nodes = new List<CharacterNode>();
        foreach (ManifestResource? scene in ReadDependencies(themePath, baseUiPath).Where(resource => resource.Type == DependencyManifest.Scene && resource.Path is not null))
        {
            string? path = layer.Resolve(scene.Path!);
            if (path is null)
            {
                continue;
            }

            foreach ((string NodeName, JsonObject Component) component in CharacterComponents(JsonNode.Parse(File.ReadAllText(path))!, character))
            {
                JsonArray size = component.Component["size"]!.AsArray();
                JsonArray pivot = component.Component["pivot"]!.AsArray();
                nodes.Add(new CharacterNode(
                    Path.GetFileName(scene.Path!),
                    component.NodeName,
                    size[0]!.GetValue<double>(),
                    size[1]!.GetValue<double>(),
                    pivot[0]!.GetValue<double>(),
                    pivot[1]!.GetValue<double>()));
            }
        }

        return nodes;
    }

    public IReadOnlyDictionary<string, CharacterTexture> GetTextures(string themePath, string? baseUiPath)
    {
        var layer = new ThemeLayer(themePath, baseUiPath);
        var textures = new Dictionary<string, CharacterTexture>(StringComparer.Ordinal);
        foreach (ManifestResource? resource in ReadDependencies(themePath, baseUiPath).Where(resource => resource.Type == DependencyManifest.TextureResource && resource.Path is not null))
        {
            string? imagePath = layer.Resolve(resource.Path!);
            string? definitionPath = layer.Resolve(Path.ChangeExtension(resource.Path!, ".json"));
            if (imagePath is null || definitionPath is null)
            {
                continue;
            }

            JsonNode? texture = JsonNode.Parse(File.ReadAllText(definitionPath))?["texture"];
            textures[resource.Id] = new CharacterTexture(
                resource.Id,
                Path.GetFileNameWithoutExtension(resource.Path!),
                imagePath,
                texture?["width"]?.GetValue<int>() ?? 0,
                texture?["height"]?.GetValue<int>() ?? 0);
        }

        return textures;
    }

    public string AddTexture(string themeDraftPath, string? baseUiPath, string relativeImagePath, string sourceImagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeImagePath);
        string resourcePath = "/" + relativeImagePath.Replace('\\', '/').TrimStart('/');
        if (!resourcePath.StartsWith("/resources/", StringComparison.Ordinal))
        {
            throw new ArgumentException("New textures must be inside resources/.", nameof(relativeImagePath));
        }

        resourcePath = resourcePath["/resources".Length..];
        var layer = new ThemeLayer(themeDraftPath, baseUiPath);
        var manifest = DependencyManifest.Load(layer);
        string textureId = ThemeTextures.Register(layer, manifest, resourcePath, sourceImagePath);
        manifest.Save(layer);
        return textureId;
    }

    public void MirrorTextures(string themeDraftPath, string? baseUiPath, IReadOnlyCollection<string> textureIds)
    {
        ArgumentNullException.ThrowIfNull(textureIds);
        var layer = new ThemeLayer(themeDraftPath, baseUiPath);
        var paths = ReadDependencies(themeDraftPath, baseUiPath)
            .Where(resource => resource.Type == DependencyManifest.TextureResource && resource.Path is not null)
            .ToDictionary(resource => resource.Id, resource => resource.Path!, StringComparer.Ordinal);
        foreach (string? textureId in textureIds.Distinct(StringComparer.Ordinal))
        {
            string resourcePath = paths.GetValueOrDefault(textureId)
                ?? throw new InvalidDataException($"Texture {textureId} is not registered in dependencies.json.");
            string sourcePath = layer.Resolve(resourcePath)
                ?? throw new FileNotFoundException($"Texture not found: resources{resourcePath}");

            using SKBitmap bitmap = DecodeUnpremultiplied(sourcePath)
                ?? throw new InvalidDataException($"resources{resourcePath} is not a valid PNG image.");
            SKImageInfo info = bitmap.Info;
            int[] pixels = MemoryMarshal.Cast<byte, int>(bitmap.GetPixelSpan()).ToArray();
            for (int y = 0; y < info.Height; y++)
            {
                Array.Reverse(pixels, y * info.Width, info.Width);
            }

            Marshal.Copy(pixels, 0, bitmap.GetPixels(), pixels.Length);

            string targetPath = layer.ThemeResourceFile(resourcePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            string temporaryPath = targetPath + ".tmp";
            using (var image = SKImage.FromBitmap(bitmap))
            using (SKData data = image.Encode(SKEncodedImageFormat.Png, 100))
            using (FileStream stream = File.Create(temporaryPath))
            {
                data.SaveTo(stream);
            }

            File.Move(temporaryPath, targetPath, true);
        }
    }

    public void SetTrackKeys(string themeDraftPath, string? baseUiPath, string animationResourcePath, string nodeName, IReadOnlyList<AnimationKey> keys, double duration)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Count == 0)
        {
            throw new ArgumentException("An animation track needs at least one key.", nameof(keys));
        }

        var layer = new ThemeLayer(themeDraftPath, baseUiPath);
        var manifest = DependencyManifest.Load(layer);
        var textureIds = manifest.OfType(DependencyManifest.TextureResource)
            .Select(resource => resource.Id)
            .ToHashSet(StringComparer.Ordinal);
        AnimationKey? unknown = keys.FirstOrDefault(key => !textureIds.Contains(key.TextureId));
        if (unknown is not null)
        {
            throw new InvalidDataException($"Texture {unknown.TextureId} is not registered in dependencies.json.");
        }

        string sourcePath = layer.Resolve(animationResourcePath)
            ?? throw new FileNotFoundException($"Animation not found: resources{animationResourcePath}");
        JsonObject animation = JsonNode.Parse(File.ReadAllText(sourcePath))!.AsObject();
        JsonObject track = TextureTracks(animation).FirstOrDefault(candidate => NodeName(candidate) == nodeName)
            ?? throw new InvalidDataException($"{Path.GetFileName(animationResourcePath)} has no texture track for {nodeName}.");

        track["keyCurves"] = new JsonArray(keys
            .OrderBy(key => key.Time)
            .Select(key => (JsonNode)new JsonObject { ["time"] = Math.Round(key.Time, 6), ["value"] = key.TextureId })
            .ToArray());
        animation["duration"] = Math.Round(duration, 6);

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string? textureId in TextureTracks(animation).SelectMany(candidate => candidate["keyCurves"]!.AsArray()).Select(key => key!["value"]!.GetValue<string>()))
        {
            counts[textureId] = counts.GetValueOrDefault(textureId) + 1;
        }

        // the animation's texture references are recounted; its other references stay as they are
        manifest.ReplaceReferences(DependencyManifest.SceneAnimation, animationResourcePath, textureIds.Contains, counts);
        layer.WriteThemeResource(animationResourcePath, EngineJson.ToIndentedString(animation));
        manifest.Save(layer);
    }

    public void SetFrameSize(string themeDraftPath, string? baseUiPath, string character, AtlasSize frameSize)
    {
        ArgumentNullException.ThrowIfNull(frameSize);
        if (frameSize.Width <= 0 || frameSize.Height <= 0)
        {
            throw new ArgumentException("The frame size must be positive.", nameof(frameSize));
        }

        // sizes are scaled from the Base, so repeated resizes do not drift
        var layer = new ThemeLayer(themeDraftPath, baseUiPath);
        string referenceRoot = baseUiPath ?? themeDraftPath;
        var reference = new ThemeLayer(referenceRoot, null);
        IReadOnlyDictionary<string, CharacterTexture> referenceTextures = GetTextures(referenceRoot, null);
        int changed = 0;
        foreach (ManifestResource? scene in ReadDependencies(themeDraftPath, baseUiPath).Where(resource => resource.Type == DependencyManifest.Scene && resource.Path is not null))
        {
            string? sourcePath = layer.Resolve(scene.Path!);
            if (sourcePath is null)
            {
                continue;
            }

            JsonNode root = JsonNode.Parse(File.ReadAllText(sourcePath))!;
            var components = CharacterComponents(root, character).ToList();
            if (components.Count == 0)
            {
                continue;
            }

            JsonNode? referenceScene = reference.ResolveInTheme(scene.Path!) is { } referencePath ? JsonNode.Parse(File.ReadAllText(referencePath)) : null;
            foreach ((string? nodeName, JsonObject? component) in components)
            {
                JsonObject referenceComponent = (referenceScene is null ? null : CharacterComponents(referenceScene, character).FirstOrDefault(candidate => candidate.NodeName == nodeName).Component)
                    ?? component;
                double referenceHeight = referenceComponent["size"]!.AsArray()[1]!.GetValue<double>();
                CharacterTexture? referenceTexture = referenceTextures.GetValueOrDefault(referenceComponent["textureId"]?.GetValue<string>() ?? "");
                double scale = referenceTexture is { Height: > 0 } ? referenceHeight / referenceTexture.Height : 1;
                double feet = (1 - referenceComponent["pivot"]!.AsArray()[1]!.GetValue<double>()) * referenceHeight;
                double height = frameSize.Height * scale;
                component["size"] = new JsonArray(frameSize.Width * scale, height);
                component["pivot"] = new JsonArray(component["pivot"]!.AsArray()[0]!.DeepClone(), 1 - (feet / height));
                changed++;
            }

            layer.WriteThemeResource(scene.Path!, EngineJson.ToCompactString(root));
        }

        if (changed == 0)
        {
            throw new InvalidDataException($"No scene node draws the character '{character}'.");
        }
    }

    private static SKBitmap? DecodeUnpremultiplied(string path)
    {
        using var codec = SKCodec.Create(path);
        if (codec is null)
        {
            return null;
        }

        var bitmap = new SKBitmap(new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        if (codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success)
        {
            bitmap.Dispose();
            return null;
        }

        return bitmap;
    }

    private static IEnumerable<JsonObject> TextureTracks(JsonObject animation)
    {
        return (animation["animations"] as JsonArray ?? [])
            .OfType<JsonObject>()
            .Where(track => track["componentType"]?.GetValue<string>() == "TextureComponent"
                && track["propertyId"]?.GetValue<string>() == "Texture"
                && track["keyCurves"] is JsonArray);
    }

    private static string NodeName(JsonObject track)
    {
        return (track["worldNodePath"] as JsonArray)?.LastOrDefault()?.GetValue<string>() ?? "";
    }

    private static IEnumerable<(string NodeName, JsonObject Component)> CharacterComponents(JsonNode root, string character)
    {
        string prefix = $"Sprite_{character}_";
        var pending = new Stack<JsonNode>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            switch (pending.Pop())
            {
                case JsonObject node:
                    if (node["name"]?.GetValue<string>() is { } name
                        && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                        && node["components"] is JsonArray components)
                    {
                        foreach (JsonObject? component in components.OfType<JsonObject>()
                            .Where(candidate => candidate["type"]?.GetValue<string>() == "TextureComponent"
                                && candidate["size"] is JsonArray { Count: 2 }
                                && candidate["pivot"] is JsonArray { Count: 2 }))
                        {
                            yield return (name, component);
                        }
                    }

                    foreach ((string _, JsonNode? child) in node)
                    {
                        if (child is not null)
                        {
                            pending.Push(child);
                        }
                    }

                    break;
                case JsonArray array:
                    foreach (JsonNode? child in array)
                    {
                        if (child is not null)
                        {
                            pending.Push(child);
                        }
                    }

                    break;
            }
        }
    }

    private static string CharacterOf(string animationResourcePath)
    {
        if (!animationResourcePath.StartsWith(LayoutPrefix, StringComparison.Ordinal))
        {
            return "";
        }

        string name = Path.GetFileNameWithoutExtension(animationResourcePath)["lyt_".Length..];
        int at = name.IndexOf('@');
        return at >= 0 ? name[..at] : name;
    }

    private static List<ManifestResource> ReadDependencies(string themePath, string? baseUiPath)
    {
        return DependencyManifest.TryLoad(new ThemeLayer(themePath, baseUiPath))?.Resources.ToList() ?? [];
    }
}
