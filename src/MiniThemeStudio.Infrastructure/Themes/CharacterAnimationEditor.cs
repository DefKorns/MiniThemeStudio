using System.Text.Json;
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
            .Where(resource => resource.Type == "SceneAnimation" && resource.Path?.StartsWith(LayoutPrefix, StringComparison.Ordinal) == true)
            .Select(resource => CharacterOf(resource.Path!))
            .Where(character => character.Length > 0 && GetNodes(themePath, baseUiPath, character).Count > 0)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    public IReadOnlyList<CharacterAnimation> GetAnimations(string themePath, string? baseUiPath, string character)
    {
        var animations = new List<CharacterAnimation>();
        foreach (var resource in ReadDependencies(themePath, baseUiPath)
            .Where(resource => resource.Type == "SceneAnimation" && resource.Path is not null && CharacterOf(resource.Path) == character)
            .OrderBy(resource => resource.Path, StringComparer.Ordinal))
        {
            var path = Resolve(themePath, baseUiPath, resource.Path!);
            if (path is null)
            {
                continue;
            }

            var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
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

            var fileName = Path.GetFileNameWithoutExtension(resource.Path!);
            var name = fileName.Contains('@') ? fileName[(fileName.IndexOf('@') + 1)..] : "main";
            animations.Add(new CharacterAnimation(resource.Path!, name, root["duration"]?.GetValue<double>() ?? 0, tracks));
        }

        return animations;
    }

    public IReadOnlyList<CharacterNode> GetNodes(string themePath, string? baseUiPath, string character)
    {
        var nodes = new List<CharacterNode>();
        foreach (var scene in ReadDependencies(themePath, baseUiPath).Where(resource => resource.Type == "Scene" && resource.Path is not null))
        {
            var path = Resolve(themePath, baseUiPath, scene.Path!);
            if (path is null)
            {
                continue;
            }

            foreach (var component in CharacterComponents(JsonNode.Parse(File.ReadAllText(path))!, character))
            {
                var size = component.Component["size"]!.AsArray();
                var pivot = component.Component["pivot"]!.AsArray();
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
        var textures = new Dictionary<string, CharacterTexture>(StringComparer.Ordinal);
        foreach (var resource in ReadDependencies(themePath, baseUiPath).Where(resource => resource.Type == "TextureResource" && resource.Path is not null))
        {
            var imagePath = Resolve(themePath, baseUiPath, resource.Path!);
            var definitionPath = Resolve(themePath, baseUiPath, Path.ChangeExtension(resource.Path!, ".json"));
            if (imagePath is null || definitionPath is null)
            {
                continue;
            }

            var texture = JsonNode.Parse(File.ReadAllText(definitionPath))?["texture"];
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
        using var bitmap = SKBitmap.Decode(sourceImagePath)
            ?? throw new InvalidDataException("The frame is not a valid PNG image.");

        var resourcePath = "/" + relativeImagePath.Replace('\\', '/').TrimStart('/');
        if (!resourcePath.StartsWith("/resources/", StringComparison.Ordinal))
        {
            throw new ArgumentException("New textures must be inside resources/.", nameof(relativeImagePath));
        }

        resourcePath = resourcePath["/resources".Length..];
        var dependencies = LoadDraftDependencies(themeDraftPath, baseUiPath);
        var resources = dependencies["resources"]!.AsArray();
        if (resources.Any(resource => resource?["path"]?.GetValue<string>() == resourcePath))
        {
            throw new IOException($"A texture is already registered at resources{resourcePath}.");
        }

        var imagePath = DraftPath(themeDraftPath, resourcePath);
        var definitionResourcePath = Path.ChangeExtension(resourcePath, ".json");
        var textureId = Guid.NewGuid().ToString();
        Directory.CreateDirectory(Path.GetDirectoryName(imagePath)!);
        File.Copy(sourceImagePath, imagePath, false);
        File.WriteAllText(
            DraftPath(themeDraftPath, definitionResourcePath),
            EngineJson.ToCompactString(new JsonObject
            {
                ["texture"] = new JsonObject { ["id"] = textureId, ["width"] = bitmap.Width, ["height"] = bitmap.Height },
                ["sprites"] = new JsonObject(),
            }));

        var nextGroup = resources.Max(resource => int.TryParse(resource?["groupId"]?.GetValue<string>(), out var group) ? group : 0) + 1;
        resources.Add(new JsonObject
        {
            ["id"] = textureId,
            ["type"] = "TextureResource",
            ["path"] = resourcePath,
            ["groupId"] = nextGroup.ToString(System.Globalization.CultureInfo.InvariantCulture),
        });
        resources.Add(new JsonObject
        {
            ["id"] = $"sheet_{textureId}",
            ["type"] = "SpriteSheet",
            ["dependencies"] = new JsonArray(new JsonObject { ["id"] = textureId, ["refCount"] = 1 }),
            ["path"] = definitionResourcePath,
            ["groupId"] = (nextGroup + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
        });
        SaveDependencies(themeDraftPath, dependencies);
        return textureId;
    }

    public void SetTrackKeys(string themeDraftPath, string? baseUiPath, string animationResourcePath, string nodeName, IReadOnlyList<AnimationKey> keys, double duration)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Count == 0)
        {
            throw new ArgumentException("An animation track needs at least one key.", nameof(keys));
        }

        var dependencies = LoadDraftDependencies(themeDraftPath, baseUiPath);
        var resources = dependencies["resources"]!.AsArray();
        var textureIds = resources
            .Where(resource => resource?["type"]?.GetValue<string>() == "TextureResource")
            .Select(resource => resource!["id"]!.GetValue<string>())
            .ToHashSet(StringComparer.Ordinal);
        var unknown = keys.FirstOrDefault(key => !textureIds.Contains(key.TextureId));
        if (unknown is not null)
        {
            throw new InvalidDataException($"Texture {unknown.TextureId} is not registered in dependencies.json.");
        }

        var sourcePath = Resolve(themeDraftPath, baseUiPath, animationResourcePath)
            ?? throw new FileNotFoundException($"Animation not found: resources{animationResourcePath}");
        var animation = JsonNode.Parse(File.ReadAllText(sourcePath))!.AsObject();
        var track = TextureTracks(animation).FirstOrDefault(candidate => NodeName(candidate) == nodeName)
            ?? throw new InvalidDataException($"{Path.GetFileName(animationResourcePath)} has no texture track for {nodeName}.");

        track["keyCurves"] = new JsonArray(keys
            .OrderBy(key => key.Time)
            .Select(key => (JsonNode)new JsonObject { ["time"] = Math.Round(key.Time, 6), ["value"] = key.TextureId })
            .ToArray());
        animation["duration"] = Math.Round(duration, 6);

        var animationResource = resources.FirstOrDefault(resource => resource?["path"]?.GetValue<string>() == animationResourcePath)
            ?? throw new InvalidDataException($"{animationResourcePath} is not registered in dependencies.json.");
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var textureId in TextureTracks(animation).SelectMany(candidate => candidate["keyCurves"]!.AsArray()).Select(key => key!["value"]!.GetValue<string>()))
        {
            counts[textureId] = counts.GetValueOrDefault(textureId) + 1;
        }

        var preserved = (animationResource["dependencies"] as JsonArray ?? [])
            .Where(dependency => !textureIds.Contains(dependency!["id"]!.GetValue<string>()))
            .Select(dependency => dependency!.DeepClone());
        animationResource["dependencies"] = new JsonArray(preserved
            .Concat(counts.Select(entry => (JsonNode)new JsonObject { ["id"] = entry.Key, ["refCount"] = entry.Value }))
            .ToArray());

        var targetPath = DraftPath(themeDraftPath, animationResourcePath);
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        EngineJson.WriteAtomically(targetPath, EngineJson.ToIndentedString(animation));
        SaveDependencies(themeDraftPath, dependencies);
    }

    public void SetFrameSize(string themeDraftPath, string? baseUiPath, string character, AtlasSize frameSize)
    {
        ArgumentNullException.ThrowIfNull(frameSize);
        if (frameSize.Width <= 0 || frameSize.Height <= 0)
        {
            throw new ArgumentException("The frame size must be positive.", nameof(frameSize));
        }

        var referenceRoot = baseUiPath ?? themeDraftPath;
        var referenceTextures = GetTextures(referenceRoot, null);
        var changed = 0;
        foreach (var scene in ReadDependencies(themeDraftPath, baseUiPath).Where(resource => resource.Type == "Scene" && resource.Path is not null))
        {
            var sourcePath = Resolve(themeDraftPath, baseUiPath, scene.Path!);
            if (sourcePath is null)
            {
                continue;
            }

            var root = JsonNode.Parse(File.ReadAllText(sourcePath))!;
            var components = CharacterComponents(root, character).ToList();
            if (components.Count == 0)
            {
                continue;
            }

            var referenceScene = ResolveIn(referenceRoot, scene.Path!) is { } referencePath ? JsonNode.Parse(File.ReadAllText(referencePath)) : null;
            foreach (var (nodeName, component) in components)
            {
                var reference = (referenceScene is null ? null : CharacterComponents(referenceScene, character).FirstOrDefault(candidate => candidate.NodeName == nodeName).Component)
                    ?? component;
                var referenceHeight = reference["size"]!.AsArray()[1]!.GetValue<double>();
                var referenceTexture = referenceTextures.GetValueOrDefault(reference["textureId"]?.GetValue<string>() ?? "");
                var scale = referenceTexture is { Height: > 0 } ? referenceHeight / referenceTexture.Height : 1;
                var feet = (1 - reference["pivot"]!.AsArray()[1]!.GetValue<double>()) * referenceHeight;
                var height = frameSize.Height * scale;
                component["size"] = new JsonArray(frameSize.Width * scale, height);
                component["pivot"] = new JsonArray(component["pivot"]!.AsArray()[0]!.DeepClone(), 1 - (feet / height));
                changed++;
            }

            var targetPath = DraftPath(themeDraftPath, scene.Path!);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            EngineJson.WriteAtomically(targetPath, EngineJson.ToCompactString(root));
        }

        if (changed == 0)
        {
            throw new InvalidDataException($"No scene node draws the character '{character}'.");
        }
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
        var prefix = $"Sprite_{character}_";
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
                        foreach (var component in components.OfType<JsonObject>()
                            .Where(candidate => candidate["type"]?.GetValue<string>() == "TextureComponent"
                                && candidate["size"] is JsonArray { Count: 2 }
                                && candidate["pivot"] is JsonArray { Count: 2 }))
                        {
                            yield return (name, component);
                        }
                    }

                    foreach (var (_, child) in node)
                    {
                        if (child is not null)
                        {
                            pending.Push(child);
                        }
                    }

                    break;
                case JsonArray array:
                    foreach (var child in array)
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

        var name = Path.GetFileNameWithoutExtension(animationResourcePath)["lyt_".Length..];
        var at = name.IndexOf('@');
        return at >= 0 ? name[..at] : name;
    }

    private static JsonObject LoadDraftDependencies(string themeDraftPath, string? baseUiPath)
    {
        var sourcePath = new[] { themeDraftPath, baseUiPath }
            .Where(root => root != null)
            .Select(root => Path.Combine(root!, "dependencies.json"))
            .FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException("dependencies.json not found in the theme or its Base.");
        return JsonNode.Parse(File.ReadAllText(sourcePath))!.AsObject();
    }

    private static void SaveDependencies(string themeDraftPath, JsonObject dependencies)
    {
        EngineJson.WriteAtomically(Path.Combine(themeDraftPath, "dependencies.json"), EngineJson.ToCompactString(dependencies));
    }

    private static List<DependencyResource> ReadDependencies(string themePath, string? baseUiPath)
    {
        var path = new[] { themePath, baseUiPath }
            .Where(root => root != null)
            .Select(root => Path.Combine(root!, "dependencies.json"))
            .FirstOrDefault(File.Exists);
        if (path is null)
        {
            return [];
        }

        return JsonNode.Parse(File.ReadAllText(path))!["resources"]!.AsArray()
            .OfType<JsonObject>()
            .Select(resource => new DependencyResource(
                resource["id"]?.GetValue<string>() ?? "",
                resource["type"]?.GetValue<string>() ?? "",
                resource["path"]?.GetValue<string>()))
            .ToList();
    }

    private static string? Resolve(string themePath, string? baseUiPath, string resourcePath)
    {
        return ResolveIn(themePath, resourcePath) ?? (baseUiPath is null ? null : ResolveIn(baseUiPath, resourcePath));
    }

    private static string? ResolveIn(string root, string resourcePath)
    {
        var path = DraftPath(root, resourcePath);
        return File.Exists(path) ? path : null;
    }

    private static string DraftPath(string root, string resourcePath)
    {
        var rootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var path = Path.GetFullPath(Path.Combine(rootPath, "resources", resourcePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(rootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Unsafe resource path: {resourcePath}");
        }

        return path;
    }

    private sealed record DependencyResource(string Id, string Type, string? Path);
}
