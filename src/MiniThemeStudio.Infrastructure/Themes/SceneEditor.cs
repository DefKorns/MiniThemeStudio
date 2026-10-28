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
        var layer = new ThemeLayer(themePath, baseUiPath);
        return (DependencyManifest.TryLoad(layer)?.OfType(DependencyManifest.Scene) ?? [])
            .Select(resource => resource.Path)
            .OfType<string>()
            .Where(path => layer.Resolve(path) is not null)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public SceneDocument Load(string themePath, string? baseUiPath, string sceneResourcePath)
    {
        var layer = new ThemeLayer(themePath, baseUiPath);
        var path = layer.Resolve(sceneResourcePath)
            ?? throw new FileNotFoundException($"Scene not found: resources{sceneResourcePath}");
        var root = ParseScene(path)["rootWorldNode"] as JsonObject
            ?? throw new InvalidDataException($"{Path.GetFileName(path)} has no rootWorldNode.");
        return new SceneDocument(sceneResourcePath, layer.ResolveInTheme(sceneResourcePath) is not null, ReadNode(root, ""));
    }

    public void EditNode(string themeDraftPath, string? baseUiPath, string sceneResourcePath, string nodePath, SceneNodeEdit edit)
    {
        ArgumentNullException.ThrowIfNull(nodePath);
        ArgumentNullException.ThrowIfNull(edit);

        var layer = new ThemeLayer(themeDraftPath, baseUiPath);
        var sourcePath = layer.Resolve(sceneResourcePath)
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

        // the Scene entry of dependencies.json counts the references of the scene to each texture
        if (replacedTextureId is not null)
        {
            var manifest = DependencyManifest.Load(layer);
            if (!manifest.IsTexture(edit.TextureId!))
            {
                throw new InvalidDataException($"Texture {edit.TextureId} is not registered in dependencies.json.");
            }

            manifest.ChangeReferenceCount(DependencyManifest.Scene, sceneResourcePath, replacedTextureId, -1);
            manifest.ChangeReferenceCount(DependencyManifest.Scene, sceneResourcePath, edit.TextureId!, 1);
            manifest.Save(layer);
        }

        layer.WriteThemeResource(sceneResourcePath, EngineJson.ToCompactString(scene));
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

    private static JsonValue Number(double value)
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
}
