using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MiniThemeStudio.Infrastructure.Themes;

internal sealed record ManifestReference(string Id, int Count);

// Content is the inline JSON some resources carry (SpriteResource frames)
internal sealed record ManifestResource(string Id, string Type, string? Path, IReadOnlyList<ManifestReference> References, string? Content);

// dependencies.json: every resource of the UI (id, type, path) and, for scenes and animations, how many times
// they reference each other resource ("dependencies" with a refCount). Edits go through the JSON tree, so the
// fields the editor does not know are written back untouched.
internal sealed class DependencyManifest
{
    public const string TextureResource = "TextureResource";
    public const string SpriteSheet = "SpriteSheet";
    public const string Scene = "Scene";
    public const string SceneAnimation = "SceneAnimation";

    private readonly JsonObject _root;
    private readonly JsonArray _resources;

    private DependencyManifest(JsonObject root, JsonArray resources)
    {
        _root = root;
        _resources = resources;
    }

    public IEnumerable<ManifestResource> Resources => _resources.OfType<JsonObject>().Select(ToResource);

    // null when neither the theme nor its Base has a dependencies.json
    public static DependencyManifest? TryLoad(ThemeLayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        return layer.DependenciesPath is { } path ? Parse(path) : null;
    }

    public static DependencyManifest Load(ThemeLayer layer)
    {
        return TryLoad(layer) ?? throw new FileNotFoundException("dependencies.json not found in the theme or its Base.");
    }

    public IEnumerable<ManifestResource> OfType(string type) => Resources.Where(resource => resource.Type == type);

    public ManifestResource? FindById(string id) => Resources.FirstOrDefault(resource => resource.Id == id);

    public ManifestResource? FindByPath(string type, string path) =>
        Resources.FirstOrDefault(resource => resource.Type == type && resource.Path == path);

    public bool IsTexture(string id) => Resources.Any(resource => resource.Id == id && resource.Type == TextureResource);

    // registers an image and its sprite sheet definition; returns the new texture id
    public string AddTexture(string imageResourcePath, string definitionResourcePath)
    {
        if (Resources.Any(resource => resource.Path == imageResourcePath))
        {
            throw new IOException($"A texture is already registered at resources{imageResourcePath}.");
        }

        string textureId = Guid.NewGuid().ToString();
        int group = NextGroupId();
        _resources.Add(new JsonObject
        {
            ["id"] = textureId,
            ["type"] = TextureResource,
            ["path"] = imageResourcePath,
            ["groupId"] = group.ToString(CultureInfo.InvariantCulture),
        });
        _resources.Add(new JsonObject
        {
            ["id"] = $"sheet_{textureId}",
            ["type"] = SpriteSheet,
            ["dependencies"] = new JsonArray(new JsonObject { ["id"] = textureId, ["refCount"] = 1 }),
            ["path"] = definitionResourcePath,
            ["groupId"] = (group + 1).ToString(CultureInfo.InvariantCulture),
        });
        return textureId;
    }

    // the scenes and animations that still draw the texture (the sprite sheet describing it does not count)
    public IReadOnlyList<string> UsersOf(string textureId) =>
        Resources
            .Where(resource => resource.Type is Scene or SceneAnimation && resource.References.Any(reference => reference.Id == textureId && reference.Count > 0))
            .Select(resource => resource.Path ?? resource.Id)
            .ToList();

    // drops the texture and the sprite sheets that only describe it
    public void RemoveTexture(string textureId)
    {
        foreach (JsonObject? resource in _resources.OfType<JsonObject>().ToList())
        {
            bool isTexture = Text(resource["id"]) == textureId && Text(resource["type"]) == TextureResource;
            bool isItsSheet = Text(resource["type"]) == SpriteSheet
                && resource["dependencies"] is JsonArray references
                && references.OfType<JsonObject>().All(reference => Text(reference["id"]) == textureId)
                && references.Count > 0;
            if (isTexture || isItsSheet)
            {
                _resources.Remove(resource);
            }
        }
    }

    // adds delta to how many times the resource at ownerPath references referencedId; a count of 0 drops the entry
    public void ChangeReferenceCount(string ownerType, string ownerPath, string referencedId, int delta)
    {
        JsonArray references = References(ownerType, ownerPath);
        JsonObject? reference = references.OfType<JsonObject>().FirstOrDefault(candidate => Text(candidate["id"]) == referencedId);
        int count = (reference is null ? 0 : Count(reference)) + delta;
        if (reference is null)
        {
            if (count > 0)
            {
                references.Add(new JsonObject { ["id"] = referencedId, ["refCount"] = count });
            }
        }
        else if (count <= 0)
        {
            references.Remove(reference);
        }
        else
        {
            reference["refCount"] = count;
        }
    }

    // replaces the references matched by isReplaced with counts, keeping the others in place
    public void ReplaceReferences(string ownerType, string ownerPath, Func<string, bool> isReplaced, IReadOnlyDictionary<string, int> counts)
    {
        ArgumentNullException.ThrowIfNull(isReplaced);
        ArgumentNullException.ThrowIfNull(counts);

        JsonObject owner = Owner(ownerType, ownerPath);
        IEnumerable<JsonNode> kept = (owner["dependencies"] as JsonArray ?? [])
            .OfType<JsonObject>()
            .Where(reference => !isReplaced(Text(reference["id"]) ?? ""))
            .Select(reference => reference.DeepClone());
        owner["dependencies"] = new JsonArray(kept
            .Concat(counts.Select(entry => (JsonNode)new JsonObject { ["id"] = entry.Key, ["refCount"] = entry.Value }))
            .ToArray());
    }

    public void Save(ThemeLayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        EngineJson.WriteAtomically(layer.ThemeDependenciesPath, EngineJson.ToCompactString(_root));
    }

    private static DependencyManifest Parse(string path)
    {
        try
        {
            if (JsonNode.Parse(File.ReadAllText(path)) is JsonObject root && root["resources"] is JsonArray resources)
            {
                return new DependencyManifest(root, resources);
            }
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"dependencies.json is not valid JSON: {exception.Message}", exception);
        }

        throw new InvalidDataException("dependencies.json has no resources list.");
    }

    private JsonArray References(string ownerType, string ownerPath)
    {
        JsonObject owner = Owner(ownerType, ownerPath);
        if (owner["dependencies"] is not JsonArray references)
        {
            references = [];
            owner["dependencies"] = references;
        }

        return references;
    }

    private JsonObject Owner(string ownerType, string ownerPath)
    {
        return _resources.OfType<JsonObject>().FirstOrDefault(resource => Text(resource["type"]) == ownerType && Text(resource["path"]) == ownerPath)
            ?? throw new InvalidDataException($"{ownerPath} is not registered in dependencies.json.");
    }

    private int NextGroupId()
    {
        return _resources.OfType<JsonObject>()
            .Select(resource => int.TryParse(Text(resource["groupId"]), NumberStyles.Integer, CultureInfo.InvariantCulture, out int group) ? group : 0)
            .DefaultIfEmpty(0)
            .Max() + 1;
    }

    private static ManifestResource ToResource(JsonObject resource)
    {
        return new ManifestResource(
            Text(resource["id"]) ?? "",
            Text(resource["type"]) ?? "",
            Text(resource["path"]),
            (resource["dependencies"] as JsonArray ?? [])
                .OfType<JsonObject>()
                .Select(reference => new ManifestReference(Text(reference["id"]) ?? "", Count(reference)))
                .ToList(),
            Text(resource["content"]));
    }

    private static int Count(JsonObject reference)
    {
        return reference["refCount"] is JsonValue value && value.TryGetValue<int>(out int count) ? count : 1;
    }

    private static string? Text(JsonNode? value)
    {
        return value is JsonValue json && json.TryGetValue<string>(out string? text) ? text : null;
    }
}
