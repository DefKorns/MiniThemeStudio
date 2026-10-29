using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MiniThemeStudio.Infrastructure.Themes;

// reading and changing the JSON tree of .scn files, shared by the editors that write scenes
internal static class SceneJson
{
    public static (ThemeLayer Layer, JsonObject Scene, JsonObject Root) OpenScene(string themeDraftPath, string? baseUiPath, string sceneResourcePath)
    {
        var layer = new ThemeLayer(themeDraftPath, baseUiPath);
        string sourcePath = layer.Resolve(sceneResourcePath)
            ?? throw new FileNotFoundException($"Scene not found: resources{sceneResourcePath}");
        JsonObject scene = ParseScene(sourcePath);
        JsonObject root = scene["rootWorldNode"] as JsonObject
            ?? throw new InvalidDataException($"{Path.GetFileName(sourcePath)} has no rootWorldNode.");
        return (layer, scene, root);
    }

    // a node drawing a texture, with the fields the console's own scenes give every node and texture;
    // repeatX tiles the texture across the width (scrolling layers wider than their image use it)
    public static JsonObject ImageNode(string name, double zIndex, string textureId, double width, double height, double repeatX = 1)
    {
        return new JsonObject
        {
            ["id"] = NewId(),
            ["name"] = name,
            ["transform"] = new JsonArray(1, 0, 0, 0, 1, 0),
            ["enabled"] = true,
            ["zIndex"] = Number(zIndex),
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
                ["size"] = new JsonArray(Number(width), Number(height)),
                ["pivot"] = new JsonArray(0.5, 0.5),
                ["uvOffset"] = new JsonArray(0, 0),
                ["uvRepeat"] = new JsonArray(Number(repeatX), 1),
            }),
            ["scriptType"] = "WorldNode",
            ["properties"] = new JsonObject(),
            ["children"] = new JsonArray(),
        };
    }

    // the Scene entry of dependencies.json counts each component that draws a texture or one of its sprites
    public static void ChangeReferenceCounts(ThemeLayer layer, string sceneResourcePath, JsonObject branch, int sign)
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
        foreach ((string? textureId, int count) in counts)
        {
            manifest.ChangeReferenceCount(DependencyManifest.Scene, sceneResourcePath, textureId, sign * count);
        }

        manifest.Save(layer);
    }

    public static string? TextureReference(JsonObject component)
    {
        return String(component["type"]) switch
        {
            "TextureComponent" => String(component["textureId"]) is { Length: > 0 } textureId ? textureId : null,

            // spriteId is "<atlas texture id>.<sprite name>"
            "SpriteComponent" => String(component["spriteId"]) is { } spriteId && spriteId.IndexOf('.', StringComparison.Ordinal) is var dot and > 0 ? spriteId[..dot] : null,
            _ => null,
        };
    }

    public static IEnumerable<JsonObject> Nodes(JsonObject node)
    {
        yield return node;
        foreach (JsonObject? descendant in (node["children"] as JsonArray ?? []).OfType<JsonObject>().SelectMany(Nodes))
        {
            yield return descendant;
        }
    }

    public static IEnumerable<JsonObject> Components(JsonObject node) => (node["components"] as JsonArray ?? []).OfType<JsonObject>();

    // everything a node holds except its child nodes
    public static IEnumerable<JsonNode> OwnFields(JsonObject node) =>
        node.Where(field => field.Key != "children" && field.Value is not null).Select(field => field.Value!);

    public static IEnumerable<JsonNode> Descendants(JsonNode node)
    {
        yield return node;
        IEnumerable<JsonNode?> children = node switch
        {
            JsonObject json => json.Select(field => field.Value),
            JsonArray array => array.AsEnumerable(),
            _ => [],
        };
        foreach (JsonNode? descendant in children.OfType<JsonNode>().SelectMany(Descendants))
        {
            yield return descendant;
        }
    }

    public static JsonArray Children(JsonObject node)
    {
        if (node["children"] is not JsonArray children)
        {
            children = [];
            node["children"] = children;
        }

        return children;
    }

    // animations find nodes by name, so siblings keep distinct names
    public static string UniqueName(JsonArray siblings, string name)
    {
        var taken = siblings.OfType<JsonObject>().Select(sibling => String(sibling["name"])).OfType<string>().ToHashSet(StringComparer.Ordinal);
        string unique = name;
        for (int number = 2; taken.Contains(unique); number++)
        {
            unique = $"{name} {number.ToString(CultureInfo.InvariantCulture)}";
        }

        return unique;
    }

    public static string ChildPath(string parentPath, int index)
    {
        string text = index.ToString(CultureInfo.InvariantCulture);
        return parentPath.Length == 0 ? text : $"{parentPath}/{text}";
    }

    public static string SafeFileName(string name)
    {
        string safe = new string(name.ToLowerInvariant().Select(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-' ? character : '_').ToArray()).Trim('_');
        return safe.Length == 0 ? "image" : safe;
    }

    public static string NewId() => Guid.NewGuid().ToString();

    public static JsonObject FindNode(JsonObject root, string path, string sceneResourcePath)
    {
        return FindNode(root, path)
            ?? throw new InvalidDataException($"Node {path} not found in {Path.GetFileName(sceneResourcePath)}.");
    }

    public static JsonObject? FindNode(JsonObject? root, string path)
    {
        JsonObject? node = root;
        foreach (string part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (node?["children"] is not JsonArray children
                || !int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out int index)
                || index >= children.Count)
            {
                return null;
            }

            node = children[index] as JsonObject;
        }

        return node;
    }

    public static JsonObject ParseScene(string path)
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

    public static JsonValue Number(double value)
    {
        return value == Math.Floor(value) && Math.Abs(value) < 1e15 ? JsonValue.Create((long)value) : JsonValue.Create(value);
    }

    public static double? Double(JsonNode? value)
    {
        return value is JsonValue json && json.TryGetValue<double>(out double number) ? number : null;
    }

    public static bool? Bool(JsonNode? value)
    {
        return value is JsonValue json && json.TryGetValue<bool>(out bool flag) ? flag : null;
    }

    public static string? String(JsonNode? value)
    {
        return value is JsonValue json && json.TryGetValue<string>(out string? text) ? text : null;
    }
}
