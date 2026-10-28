using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using MiniThemeStudio.Core.Scenes;

namespace MiniThemeStudio.Infrastructure.Themes;

// edits .scn files in place through JsonNode, so every field the editor does not know is written back untouched
public sealed class SceneEditor : ISceneEditor
{
    // where ImportTexture puts the PNGs added to scenes, next to the console's own layout images
    private const string ImportedImagesFolder = "/layout/asset/sprite/scene";

    // pixels a duplicate moves right and down from the original
    private const double DuplicateOffset = 16;

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

        var (layer, scene, root) = OpenScene(themeDraftPath, baseUiPath, sceneResourcePath);
        var node = FindNode(root, nodePath, sceneResourcePath);

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

    public string ImportTexture(string themeDraftPath, string? baseUiPath, string sourceImagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceImagePath);

        var layer = new ThemeLayer(themeDraftPath, baseUiPath);
        var manifest = DependencyManifest.Load(layer);
        var name = SafeFileName(Path.GetFileNameWithoutExtension(sourceImagePath));
        var resourcePath = $"{ImportedImagesFolder}/{name}.png";
        for (var number = 2; manifest.Resources.Any(resource => resource.Path == resourcePath) || layer.Resolve(resourcePath) is not null; number++)
        {
            resourcePath = $"{ImportedImagesFolder}/{name}_{number.ToString(CultureInfo.InvariantCulture)}.png";
        }

        var textureId = ThemeTextures.Register(layer, manifest, resourcePath, sourceImagePath);
        manifest.Save(layer);
        return textureId;
    }

    public string AddImageNode(string themeDraftPath, string? baseUiPath, string sceneResourcePath, string parentPath, string textureId, string name)
    {
        ArgumentNullException.ThrowIfNull(parentPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(textureId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var (layer, scene, root) = OpenScene(themeDraftPath, baseUiPath, sceneResourcePath);
        var parent = FindNode(root, parentPath, sceneResourcePath);
        var manifest = DependencyManifest.Load(layer);
        if (!manifest.IsTexture(textureId))
        {
            throw new InvalidDataException($"Texture {textureId} is not registered in dependencies.json.");
        }

        var (width, height) = ThemeTextures.Size(layer, manifest, textureId)
            ?? throw new FileNotFoundException($"The image of texture {textureId} was not found.");
        var children = Children(parent);
        var topLayer = children.OfType<JsonObject>().Select(child => Double(child["zIndex"]) ?? 0).DefaultIfEmpty(-1).Max();

        // the fields the console's own scenes give every node and texture
        children.Add(new JsonObject
        {
            ["id"] = NewId(),
            ["name"] = UniqueName(children, name.Trim()),
            ["transform"] = new JsonArray(1, 0, 0, 0, 1, 0),
            ["enabled"] = true,
            ["zIndex"] = Number(Math.Floor(topLayer) + 1),
            ["visible"] = true,
            ["color"] = new JsonArray(1, 1, 1, 1),
            ["category"] = 0,
            ["components"] = new JsonArray(new JsonObject
            {
                ["type"] = "TextureComponent",
                ["textureId"] = textureId,
                ["blendMode"] = 0,
                ["color"] = new JsonArray(1, 1, 1, 1),
                ["enabled"] = true,
                ["horizontalWrapMode"] = 0,
                ["id"] = NewId(),
                ["magFilterMode"] = 0,
                ["minFilterMode"] = 0,
                ["scriptType"] = "TextureComponent",
                ["properties"] = new JsonObject(),
                ["verticalWrapMode"] = 0,
                ["viewDepth"] = 0,
                ["visible"] = true,
                ["layer"] = 0,
                ["size"] = new JsonArray(width, height),
                ["pivot"] = new JsonArray(0.5, 0.5),
                ["uvOffset"] = new JsonArray(0, 0),
                ["uvRepeat"] = new JsonArray(1, 1),
            }),
            ["scriptType"] = "WorldNode",
            ["properties"] = new JsonObject(),
            ["children"] = new JsonArray(),
        });

        manifest.ChangeReferenceCount(DependencyManifest.Scene, sceneResourcePath, textureId, 1);
        manifest.Save(layer);
        layer.WriteThemeResource(sceneResourcePath, EngineJson.ToCompactString(scene));
        return ChildPath(parentPath, children.Count - 1);
    }

    public string DuplicateNode(string themeDraftPath, string? baseUiPath, string sceneResourcePath, string nodePath)
    {
        var (layer, scene, root) = OpenScene(themeDraftPath, baseUiPath, sceneResourcePath);
        var (parentPath, index) = SplitPath(nodePath, "duplicated");
        var siblings = Children(FindNode(root, parentPath, sceneResourcePath));
        var original = FindNode(root, nodePath, sceneResourcePath);

        // the copy gets ids of its own; links between its nodes follow it, links to the rest of the scene stay
        var copy = (JsonObject)original.DeepClone();
        var newIds = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var owner in Nodes(copy).SelectMany(node => Components(node).Prepend(node)))
        {
            if (String(owner["id"]) is { } id)
            {
                newIds[id] = NewId();
                owner["id"] = newIds[id];
            }
        }

        foreach (var link in Descendants(copy).OfType<JsonObject>())
        {
            if (String(link["linkId"]) is { } linkId && newIds.TryGetValue(linkId, out var newId))
            {
                link["linkId"] = newId;
            }
        }

        copy["name"] = UniqueName(siblings, $"{String(original["name"]) ?? "node"} copy");
        if (copy["transform"] is JsonArray { Count: 6 } transform && Double(transform[2]) is { } x && Double(transform[5]) is { } y)
        {
            transform[2] = Number(x + DuplicateOffset);
            transform[5] = Number(y - DuplicateOffset);
        }

        siblings.Insert(index + 1, copy);
        ChangeReferenceCounts(layer, sceneResourcePath, copy, 1);
        layer.WriteThemeResource(sceneResourcePath, EngineJson.ToCompactString(scene));
        return ChildPath(parentPath, index + 1);
    }

    public void DeleteNode(string themeDraftPath, string? baseUiPath, string sceneResourcePath, string nodePath)
    {
        var (layer, scene, root) = OpenScene(themeDraftPath, baseUiPath, sceneResourcePath);
        var (parentPath, index) = SplitPath(nodePath, "deleted");
        var siblings = Children(FindNode(root, parentPath, sceneResourcePath));
        var node = FindNode(root, nodePath, sceneResourcePath);

        // scripts reach nodes through links in the properties of other nodes: removing a linked node breaks them
        var removedIds = Nodes(node).SelectMany(removed => Components(removed).Prepend(removed))
            .Select(owner => String(owner["id"]))
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        siblings.RemoveAt(index);
        var linking = Nodes(root).FirstOrDefault(other => OwnFields(other).SelectMany(Descendants).OfType<JsonObject>()
            .Any(link => String(link["linkId"]) is { } linkId && removedIds.Contains(linkId)));
        if (linking is not null)
        {
            throw new InvalidDataException(
                $"{String(node["name"])} cannot be deleted: {String(linking["name"])} links to it, so the console scripts use it. Hide it instead.");
        }

        ChangeReferenceCounts(layer, sceneResourcePath, node, -1);
        layer.WriteThemeResource(sceneResourcePath, EngineJson.ToCompactString(scene));
    }

    private static (ThemeLayer Layer, JsonObject Scene, JsonObject Root) OpenScene(string themeDraftPath, string? baseUiPath, string sceneResourcePath)
    {
        var layer = new ThemeLayer(themeDraftPath, baseUiPath);
        var sourcePath = layer.Resolve(sceneResourcePath)
            ?? throw new FileNotFoundException($"Scene not found: resources{sceneResourcePath}");
        var scene = ParseScene(sourcePath);
        var root = scene["rootWorldNode"] as JsonObject
            ?? throw new InvalidDataException($"{Path.GetFileName(sourcePath)} has no rootWorldNode.");
        return (layer, scene, root);
    }

    // the Scene entry of dependencies.json counts each component that draws a texture or one of its sprites
    private static void ChangeReferenceCounts(ThemeLayer layer, string sceneResourcePath, JsonObject branch, int sign)
    {
        var counts = Nodes(branch).SelectMany(Components)
            .Select(TextureReference)
            .OfType<string>()
            .CountBy(textureId => textureId, StringComparer.Ordinal)
            .ToList();
        if (counts.Count == 0)
        {
            return;
        }

        var manifest = DependencyManifest.Load(layer);
        foreach (var (textureId, count) in counts)
        {
            manifest.ChangeReferenceCount(DependencyManifest.Scene, sceneResourcePath, textureId, sign * count);
        }

        manifest.Save(layer);
    }

    private static string? TextureReference(JsonObject component)
    {
        return String(component["type"]) switch
        {
            "TextureComponent" => String(component["textureId"]) is { Length: > 0 } textureId ? textureId : null,

            // spriteId is "<atlas texture id>.<sprite name>"
            "SpriteComponent" => String(component["spriteId"]) is { } spriteId && spriteId.IndexOf('.', StringComparison.Ordinal) is var dot and > 0 ? spriteId[..dot] : null,
            _ => null,
        };
    }

    private static IEnumerable<JsonObject> Nodes(JsonObject node)
    {
        yield return node;
        foreach (var descendant in (node["children"] as JsonArray ?? []).OfType<JsonObject>().SelectMany(Nodes))
        {
            yield return descendant;
        }
    }

    private static IEnumerable<JsonObject> Components(JsonObject node) => (node["components"] as JsonArray ?? []).OfType<JsonObject>();

    // everything a node holds except its child nodes
    private static IEnumerable<JsonNode> OwnFields(JsonObject node) =>
        node.Where(field => field.Key != "children" && field.Value is not null).Select(field => field.Value!);

    private static IEnumerable<JsonNode> Descendants(JsonNode node)
    {
        yield return node;
        var children = node switch
        {
            JsonObject json => json.Select(field => field.Value),
            JsonArray array => array.AsEnumerable(),
            _ => [],
        };
        foreach (var descendant in children.OfType<JsonNode>().SelectMany(Descendants))
        {
            yield return descendant;
        }
    }

    private static JsonArray Children(JsonObject node)
    {
        if (node["children"] is not JsonArray children)
        {
            children = [];
            node["children"] = children;
        }

        return children;
    }

    // animations find nodes by name, so siblings keep distinct names
    private static string UniqueName(JsonArray siblings, string name)
    {
        var taken = siblings.OfType<JsonObject>().Select(sibling => String(sibling["name"])).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var unique = name;
        for (var number = 2; taken.Contains(unique); number++)
        {
            unique = $"{name} {number.ToString(CultureInfo.InvariantCulture)}";
        }

        return unique;
    }

    private static (string ParentPath, int Index) SplitPath(string nodePath, string action)
    {
        ArgumentNullException.ThrowIfNull(nodePath);
        if (nodePath.Length == 0)
        {
            throw new ArgumentException($"The root node cannot be {action}.", nameof(nodePath));
        }

        var slash = nodePath.LastIndexOf('/');
        return int.TryParse(nodePath[(slash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var index)
            ? (slash < 0 ? "" : nodePath[..slash], index)
            : throw new InvalidDataException($"Node {nodePath} not found.");
    }

    private static string ChildPath(string parentPath, int index)
    {
        var text = index.ToString(CultureInfo.InvariantCulture);
        return parentPath.Length == 0 ? text : $"{parentPath}/{text}";
    }

    private static string SafeFileName(string name)
    {
        var safe = new string(name.ToLowerInvariant().Select(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-' ? character : '_').ToArray()).Trim('_');
        return safe.Length == 0 ? "image" : safe;
    }

    private static string NewId() => Guid.NewGuid().ToString();

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

    private static JsonObject FindNode(JsonObject root, string path, string sceneResourcePath)
    {
        return FindNode(root, path)
            ?? throw new InvalidDataException($"Node {path} not found in {Path.GetFileName(sceneResourcePath)}.");
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
