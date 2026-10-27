using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using MiniThemeStudio.Core.Scenes;

namespace MiniThemeStudio.Infrastructure.Themes;

// edits .scn files in place through JsonNode, so every field the editor does not know is written back untouched
public sealed class SceneEditor : ISceneEditor
{
    public IReadOnlyList<string> GetScenes(string themePath, string? baseUiPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themePath);
        if (FindDependencies(themePath, baseUiPath) is not { } dependenciesPath)
        {
            return [];
        }

        return (JsonNode.Parse(File.ReadAllText(dependenciesPath))?["resources"] as JsonArray ?? [])
            .OfType<JsonObject>()
            .Where(resource => resource["type"]?.GetValue<string>() == "Scene")
            .Select(resource => resource["path"]?.GetValue<string>())
            .OfType<string>()
            .Where(path => Resolve(themePath, baseUiPath, path) is not null)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public SceneDocument Load(string themePath, string? baseUiPath, string sceneResourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themePath);
        var path = Resolve(themePath, baseUiPath, sceneResourcePath)
            ?? throw new FileNotFoundException($"Scene not found: resources{sceneResourcePath}");
        var root = ParseScene(path)["rootWorldNode"] as JsonObject
            ?? throw new InvalidDataException($"{Path.GetFileName(path)} has no rootWorldNode.");
        return new SceneDocument(sceneResourcePath, ResolveIn(themePath, sceneResourcePath) is not null, ReadNode(root, ""));
    }

    public void EditNode(string themeDraftPath, string? baseUiPath, string sceneResourcePath, string nodePath, SceneNodeEdit edit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themeDraftPath);
        ArgumentNullException.ThrowIfNull(nodePath);
        ArgumentNullException.ThrowIfNull(edit);

        var sourcePath = Resolve(themeDraftPath, baseUiPath, sceneResourcePath)
            ?? throw new FileNotFoundException($"Scene not found: resources{sceneResourcePath}");
        var scene = ParseScene(sourcePath);
        var node = FindNode(scene["rootWorldNode"] as JsonObject, nodePath)
            ?? throw new InvalidDataException($"Node {nodePath} not found in {Path.GetFileName(sceneResourcePath)}.");

        if (edit.Name is { } name)
        {
            if (name.Trim().Length == 0)
            {
                throw new ArgumentException("A node needs a name.", nameof(edit));
            }

            node["name"] = name.Trim();
        }

        if (edit.X is not null || edit.Y is not null)
        {
            if (node["transform"] is not JsonArray { Count: 6 } transform)
            {
                transform = new JsonArray(1, 0, 0, 0, 1, 0);
                node["transform"] = transform;
            }

            if (edit.X is { } x)
            {
                transform[2] = Number(x);
            }

            if (edit.Y is { } y)
            {
                transform[5] = Number(y);
            }
        }

        if (edit.ZIndex is { } zIndex)
        {
            node["zIndex"] = zIndex;
        }

        if (edit.Visible is { } visible)
        {
            node["visible"] = visible;
        }

        string? replacedTextureId = null;
        if (edit.TextureId is not null || edit.Width is not null || edit.Height is not null)
        {
            var component = VisualComponent(node)
                ?? throw new InvalidDataException($"{node["name"]} does not draw an image.");
            if (edit.TextureId is { } textureId && component["type"]?.GetValue<string>() == "TextureComponent")
            {
                var current = component["textureId"]?.GetValue<string>();
                if (current != textureId)
                {
                    replacedTextureId = current ?? "";
                    component["textureId"] = textureId;
                }
            }
            else if (edit.TextureId is not null)
            {
                throw new InvalidDataException($"{node["name"]} draws an atlas sprite; change it in the Sprites tab.");
            }

            if (edit.Width is not null || edit.Height is not null)
            {
                if (component["size"] is not JsonArray { Count: 2 } size)
                {
                    size = new JsonArray(0, 0);
                    component["size"] = size;
                }

                if (edit.Width is { } width)
                {
                    size[0] = Number(width);
                }

                if (edit.Height is { } height)
                {
                    size[1] = Number(height);
                }
            }
        }

        if (replacedTextureId is not null)
        {
            ReplaceSceneDependency(themeDraftPath, baseUiPath, sceneResourcePath, replacedTextureId, edit.TextureId!);
        }

        var targetPath = DraftPath(themeDraftPath, sceneResourcePath);
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        EngineJson.WriteAtomically(targetPath, EngineJson.ToCompactString(scene));
    }

    private static SceneNode ReadNode(JsonObject node, string path)
    {
        var components = (node["components"] as JsonArray ?? []).OfType<JsonObject>().ToList();
        var children = (node["children"] as JsonArray ?? []).OfType<JsonObject>()
            .Select((child, index) => ReadNode(child, path.Length == 0 ? index.ToString(CultureInfo.InvariantCulture) : $"{path}/{index}"))
            .ToList();

        return new SceneNode(
            path,
            String(node["name"]) ?? "",
            ReadTransform(node["transform"]),
            (int)Math.Round(Double(node["zIndex"]) ?? 0),
            Bool(node["visible"]) ?? true,
            Bool(node["enabled"]) ?? true,
            components.Select(component => String(component["type"]) ?? "?").ToList(),
            ReadVisual(components),
            children);
    }

    private static SceneTransform ReadTransform(JsonNode? value)
    {
        if (value is not JsonArray { Count: 6 } transform)
        {
            return SceneTransform.Identity;
        }

        double At(int index) => Double(transform[index]) ?? 0;
        return new SceneTransform(At(0), At(1), At(2), At(3), At(4), At(5));
    }

    private static SceneVisual? ReadVisual(IReadOnlyList<JsonObject> components)
    {
        foreach (var component in components)
        {
            var kind = String(component["type"]) switch
            {
                "TextureComponent" => SceneVisualKind.Texture,
                "SpriteComponent" => SceneVisualKind.Sprite,
                "LabelComponent" => SceneVisualKind.Label,
                _ => (SceneVisualKind?)null,
            };
            if (kind is null)
            {
                continue;
            }

            var size = Pair(component["size"]);
            var pivot = Pair(component["pivot"]);
            var repeat = Pair(component["uvRepeat"]);
            var color = component["color"] as JsonArray;
            return new SceneVisual(
                kind.Value,
                String(component["textureId"]),
                String(component["spriteId"]),
                String(component["text"]),
                size?.First ?? 0,
                size?.Second ?? 0,
                pivot?.First ?? double.NaN,
                pivot?.Second ?? double.NaN,
                repeat?.First ?? 1,
                repeat?.Second ?? 1,
                Bool(component["hFlip"]) ?? false,
                color is { Count: 4 } ? Double(color[3]) ?? 1 : 1);
        }

        return null;
    }

    private static JsonObject? VisualComponent(JsonObject node)
    {
        return (node["components"] as JsonArray ?? []).OfType<JsonObject>()
            .FirstOrDefault(component => String(component["type"]) is "TextureComponent" or "SpriteComponent");
    }

    private static JsonObject? FindNode(JsonObject? root, string path)
    {
        var node = root;
        foreach (var part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (node?["children"] is not JsonArray children
                || !int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
                || index >= children.Count)
            {
                return null;
            }

            node = children[index] as JsonObject;
        }

        return node;
    }

    // the Scene entry of dependencies.json counts the references of the scene to each texture
    private static void ReplaceSceneDependency(string themeDraftPath, string? baseUiPath, string sceneResourcePath, string oldTextureId, string newTextureId)
    {
        var dependenciesPath = FindDependencies(themeDraftPath, baseUiPath)
            ?? throw new FileNotFoundException("dependencies.json not found in the theme or its Base.");
        var dependencies = JsonNode.Parse(File.ReadAllText(dependenciesPath))!.AsObject();
        var resources = dependencies["resources"]!.AsArray().OfType<JsonObject>().ToList();
        if (!resources.Any(resource => String(resource["id"]) == newTextureId && String(resource["type"]) == "TextureResource"))
        {
            throw new InvalidDataException($"Texture {newTextureId} is not registered in dependencies.json.");
        }

        var sceneResource = resources.FirstOrDefault(resource => String(resource["type"]) == "Scene" && String(resource["path"]) == sceneResourcePath)
            ?? throw new InvalidDataException($"{sceneResourcePath} is not registered in dependencies.json.");
        if (sceneResource["dependencies"] is not JsonArray references)
        {
            references = [];
            sceneResource["dependencies"] = references;
        }

        if (references.OfType<JsonObject>().FirstOrDefault(reference => String(reference["id"]) == oldTextureId) is { } old)
        {
            var count = (int)(Double(old["refCount"]) ?? 1) - 1;
            if (count <= 0)
            {
                references.Remove(old);
            }
            else
            {
                old["refCount"] = count;
            }
        }

        if (references.OfType<JsonObject>().FirstOrDefault(reference => String(reference["id"]) == newTextureId) is { } added)
        {
            added["refCount"] = (int)(Double(added["refCount"]) ?? 0) + 1;
        }
        else
        {
            references.Add(new JsonObject { ["id"] = newTextureId, ["refCount"] = 1 });
        }

        EngineJson.WriteAtomically(Path.Combine(themeDraftPath, "dependencies.json"), EngineJson.ToCompactString(dependencies));
    }

    private static JsonObject ParseScene(string path)
    {
        try
        {
            return JsonNode.Parse(File.ReadAllText(path)) as JsonObject
                ?? throw new InvalidDataException($"{Path.GetFileName(path)} is not a JSON object.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"{Path.GetFileName(path)} is not valid JSON: {exception.Message}", exception);
        }
    }

    private static JsonNode Number(double value)
    {
        return value == Math.Floor(value) && Math.Abs(value) < 1e15 ? JsonValue.Create((long)value) : JsonValue.Create(value);
    }

    private static (double First, double Second)? Pair(JsonNode? value)
    {
        return value is JsonArray { Count: 2 } pair && Double(pair[0]) is { } first && Double(pair[1]) is { } second ? (first, second) : null;
    }

    private static double? Double(JsonNode? value)
    {
        return value is JsonValue json && json.TryGetValue<double>(out var number) ? number : null;
    }

    private static bool? Bool(JsonNode? value)
    {
        return value is JsonValue json && json.TryGetValue<bool>(out var flag) ? flag : null;
    }

    private static string? String(JsonNode? value)
    {
        return value is JsonValue json && json.TryGetValue<string>(out var text) ? text : null;
    }

    private static string? FindDependencies(string themePath, string? baseUiPath)
    {
        return new[] { themePath, baseUiPath }
            .Where(root => root != null)
            .Select(root => Path.Combine(root!, "dependencies.json"))
            .FirstOrDefault(File.Exists);
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
}
