using System.Globalization;
using System.Text.Json.Nodes;
using MiniThemeStudio.Core.Scenes;
using static MiniThemeStudio.Infrastructure.Themes.SceneJson;

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
        string path = layer.Resolve(sceneResourcePath)
            ?? throw new FileNotFoundException($"Scene not found: resources{sceneResourcePath}");
        JsonObject root = ParseScene(path)["rootWorldNode"] as JsonObject
            ?? throw new InvalidDataException($"{Path.GetFileName(path)} has no rootWorldNode.");
        return new SceneDocument(sceneResourcePath, layer.ResolveInTheme(sceneResourcePath) is not null, ReadNode(root, ""));
    }

    public void EditNode(string themeDraftPath, string? baseUiPath, string sceneResourcePath, string nodePath, SceneNodeEdit edit)
    {
        ArgumentNullException.ThrowIfNull(nodePath);
        ArgumentNullException.ThrowIfNull(edit);

        (ThemeLayer? layer, JsonObject? scene, JsonObject? root) = OpenScene(themeDraftPath, baseUiPath, sceneResourcePath);
        JsonObject node = FindNode(root, nodePath, sceneResourcePath);

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
            JsonObject component = VisualComponent(node)
                ?? throw new InvalidDataException($"{node["name"]} does not draw an image.");
            if (edit.TextureId is { } textureId && component["type"]?.GetValue<string>() == "TextureComponent")
            {
                string? current = component["textureId"]?.GetValue<string>();
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

    public IReadOnlyList<SceneAnimationBinding> GetAnimations(string themePath, string? baseUiPath, SceneDocument scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var layer = new ThemeLayer(themePath, baseUiPath);

        // the console's animators are the nodes with an AnimatorComponent; any node may hold the tracks' nodes
        var candidates = scene.Root.SelfAndDescendants()
            .OrderByDescending(node => node.ComponentTypes.Contains("AnimatorComponent"))
            .ToList();
        var bindings = new List<SceneAnimationBinding>();
        foreach (string? path in (DependencyManifest.TryLoad(layer)?.OfType(DependencyManifest.SceneAnimation) ?? []).Select(resource => resource.Path).OfType<string>().Distinct(StringComparer.Ordinal))
        {
            if (layer.Resolve(path) is not { } file || ReadClip(path, file) is not { Tracks.Count: > 0 } clip)
            {
                continue;
            }

            if (candidates.FirstOrDefault(node => clip.Tracks.All(track => SceneAnimationEvaluator.Resolve(node, track.NodePath) is not null)) is { } animator)
            {
                bindings.Add(new SceneAnimationBinding(clip, animator.Path, animator.Name));
            }
        }

        return bindings.OrderBy(binding => binding.Clip.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public string ImportTexture(string themeDraftPath, string? baseUiPath, string sourceImagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceImagePath);

        var layer = new ThemeLayer(themeDraftPath, baseUiPath);
        var manifest = DependencyManifest.Load(layer);
        string resourcePath = ThemeTextures.FreeResourcePath(layer, manifest, ImportedImagesFolder, SafeFileName(Path.GetFileNameWithoutExtension(sourceImagePath)));
        string textureId = ThemeTextures.Register(layer, manifest, resourcePath, sourceImagePath);
        manifest.Save(layer);
        return textureId;
    }

    public string AddImageNode(string themeDraftPath, string? baseUiPath, string sceneResourcePath, string parentPath, string textureId, string name)
    {
        ArgumentNullException.ThrowIfNull(parentPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(textureId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        (ThemeLayer? layer, JsonObject? scene, JsonObject? root) = OpenScene(themeDraftPath, baseUiPath, sceneResourcePath);
        JsonObject parent = FindNode(root, parentPath, sceneResourcePath);
        var manifest = DependencyManifest.Load(layer);
        if (!manifest.IsTexture(textureId))
        {
            throw new InvalidDataException($"Texture {textureId} is not registered in dependencies.json.");
        }

        (int width, int height) = ThemeTextures.Size(layer, manifest, textureId)
            ?? throw new FileNotFoundException($"The image of texture {textureId} was not found.");
        JsonArray children = Children(parent);
        double topLayer = children.OfType<JsonObject>().Select(child => Double(child["zIndex"]) ?? 0).DefaultIfEmpty(-1).Max();

        children.Add(ImageNode(UniqueName(children, name.Trim()), Math.Floor(topLayer) + 1, textureId, width, height));

        manifest.ChangeReferenceCount(DependencyManifest.Scene, sceneResourcePath, textureId, 1);
        manifest.Save(layer);
        layer.WriteThemeResource(sceneResourcePath, EngineJson.ToCompactString(scene));
        return ChildPath(parentPath, children.Count - 1);
    }

    public string DuplicateNode(string themeDraftPath, string? baseUiPath, string sceneResourcePath, string nodePath)
    {
        (ThemeLayer? layer, JsonObject? scene, JsonObject? root) = OpenScene(themeDraftPath, baseUiPath, sceneResourcePath);
        (string? parentPath, int index) = SplitPath(nodePath, "duplicated");
        JsonArray siblings = Children(FindNode(root, parentPath, sceneResourcePath));
        JsonObject original = FindNode(root, nodePath, sceneResourcePath);

        // the copy gets ids of its own; links between its nodes follow it, links to the rest of the scene stay
        var copy = (JsonObject)original.DeepClone();
        var newIds = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (JsonObject? owner in Nodes(copy).SelectMany(node => Components(node).Prepend(node)))
        {
            if (String(owner["id"]) is { } id)
            {
                newIds[id] = NewId();
                owner["id"] = newIds[id];
            }
        }

        foreach (JsonObject link in Descendants(copy).OfType<JsonObject>())
        {
            if (String(link["linkId"]) is { } linkId && newIds.TryGetValue(linkId, out string? newId))
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
        (ThemeLayer? layer, JsonObject? scene, JsonObject? root) = OpenScene(themeDraftPath, baseUiPath, sceneResourcePath);
        (string? parentPath, int index) = SplitPath(nodePath, "deleted");
        JsonArray siblings = Children(FindNode(root, parentPath, sceneResourcePath));
        JsonObject node = FindNode(root, nodePath, sceneResourcePath);

        // scripts reach nodes through links in the properties of other nodes: removing a linked node breaks them
        var removedIds = Nodes(node).SelectMany(removed => Components(removed).Prepend(removed))
            .Select(owner => String(owner["id"]))
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        siblings.RemoveAt(index);
        JsonObject? linking = Nodes(root).FirstOrDefault(other => OwnFields(other).SelectMany(Descendants).OfType<JsonObject>()
            .Any(link => String(link["linkId"]) is { } linkId && removedIds.Contains(linkId)));
        if (linking is not null)
        {
            throw new InvalidDataException(
                $"{String(node["name"])} cannot be deleted: {String(linking["name"])} links to it, so the console scripts use it. Hide it instead.");
        }

        ChangeReferenceCounts(layer, sceneResourcePath, node, -1);
        layer.WriteThemeResource(sceneResourcePath, EngineJson.ToCompactString(scene));
    }

    private static (string ParentPath, int Index) SplitPath(string nodePath, string action)
    {
        ArgumentNullException.ThrowIfNull(nodePath);
        if (nodePath.Length == 0)
        {
            throw new ArgumentException($"The root node cannot be {action}.", nameof(nodePath));
        }

        int slash = nodePath.LastIndexOf('/');
        return int.TryParse(nodePath[(slash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out int index)
            ? (slash < 0 ? "" : nodePath[..slash], index)
            : throw new InvalidDataException($"Node {nodePath} not found.");
    }

    private static SceneAnimationClip? ReadClip(string resourcePath, string file)
    {
        JsonObject animation = ParseScene(file);
        var tracks = (animation["animations"] as JsonArray ?? []).OfType<JsonObject>()
            .Where(track => track["worldNodePath"] is JsonArray { Count: > 0 } && String(track["propertyId"]) is not null)
            .Select(track => new SceneAnimationTrack(
                ((JsonArray)track["worldNodePath"]!).Select(String).OfType<string>().ToList(),
                String(track["propertyId"])!,
                String(track["componentType"]),
                (track["keyCurves"] as JsonArray ?? []).OfType<JsonObject>().Select(ReadKey).OrderBy(key => key.Time).ToList()))
            .ToList();
        return new SceneAnimationClip(resourcePath, Double(animation["duration"]) ?? 0, tracks);
    }

    // "value" is a number, a texture id or a flag held until the next key, or [from, to] ("linear"),
    // or [from, to, tangent out, tangent in] ("hermite")
    private static CurveKey ReadKey(JsonObject key)
    {
        double time = Double(key["time"]) ?? 0;
        if (key["value"] is JsonArray curve)
        {
            double At(int index) => index < curve.Count ? Double(curve[index]) ?? 0 : 0;
            CurveInterpolation interpolation = String(key["interp"]) == "hermite" && curve.Count >= 4 ? CurveInterpolation.Hermite : CurveInterpolation.Linear;
            return new CurveKey(time, interpolation, At(0), At(1), At(2), At(3));
        }

        return new CurveKey(time, CurveInterpolation.Constant, Double(key["value"]) ?? 0, Text: String(key["value"]), Flag: Bool(key["value"]));
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
            children,
            node["color"] is JsonArray { Count: 4 } color ? Double(color[3]) ?? 1 : 1);
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
        foreach (JsonObject component in components)
        {
            SceneVisualKind? kind = String(component["type"]) switch
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

            (double First, double Second)? size = Pair(component["size"]);
            (double First, double Second)? pivot = Pair(component["pivot"]);
            (double First, double Second)? repeat = Pair(component["uvRepeat"]);
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
                color is { Count: 4 } ? Double(color[3]) ?? 1 : 1,
                Pair(component["uvOffset"])?.First ?? 0);
        }

        return null;
    }

    private static JsonObject? VisualComponent(JsonObject node)
    {
        return (node["components"] as JsonArray ?? []).OfType<JsonObject>()
            .FirstOrDefault(component => String(component["type"]) is "TextureComponent" or "SpriteComponent");
    }

    private static (double First, double Second)? Pair(JsonNode? value)
    {
        return value is JsonArray { Count: 2 } pair && Double(pair[0]) is { } first && Double(pair[1]) is { } second ? (first, second) : null;
    }
}
