using System.Text.Json.Nodes;
using MiniThemeStudio.Core.Scenes;
using SkiaSharp;
using static MiniThemeStudio.Infrastructure.Themes.SceneJson;

namespace MiniThemeStudio.Infrastructure.Themes;

// The scrolling backgrounds of the SNES UIs: a scene animation slides the texture of each layer
// (TextureOffsetX from 0 to the number of loops over the cycle), so a wider layer moves more pixels in the
// same time and seems nearer. The NES and Famicom UIs draw their background from a Lua script instead.
public sealed class ParallaxEditor(ISceneEditor sceneEditor) : IParallaxEditor
{
    // the new layers sit with the console's own, which are in this folder
    private const string WallpaperFolder = "/layout/asset/sprite/wallpaper";
    private const string OffsetProperty = "TextureOffsetX";
    private const double ScreenWidth = 1280;

    public IReadOnlyList<ParallaxBackground> GetBackgrounds(string themePath, string? baseUiPath)
    {
        var layer = new ThemeLayer(themePath, baseUiPath);
        if (DependencyManifest.TryLoad(layer) is not { } manifest)
        {
            return [];
        }

        var scenes = manifest.OfType(DependencyManifest.Scene)
            .Select(resource => resource.Path)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Select(path => (Path: path, File: layer.Resolve(path)))
            .Where(scene => scene.File is not null)
            .Select(scene => (scene.Path, Root: ParseScene(scene.File!)["rootWorldNode"] as JsonObject))
            .Where(scene => scene.Root is not null)
            .ToList();

        var backgrounds = new List<ParallaxBackground>();
        foreach (var animationPath in manifest.OfType(DependencyManifest.SceneAnimation).Select(resource => resource.Path).OfType<string>().Distinct(StringComparer.Ordinal))
        {
            if (layer.Resolve(animationPath) is not { } animationFile)
            {
                continue;
            }

            var animation = ParseScene(animationFile);
            var tracks = OffsetTracks(animation).ToList();
            if (tracks.Count == 0)
            {
                continue;
            }

            // the node that plays the animation holds every node its tracks name
            var cycle = Double(animation["duration"]) ?? 0;
            var match = scenes
                .SelectMany(scene => IndexedNodes(scene.Root!, "").Select(node => (scene.Path, Root: scene.Root!, Animator: node)))
                .FirstOrDefault(candidate => tracks.All(track => Child(candidate.Animator.Node, TrackName(track)) is not null));
            if (match.Animator.Node is not { } animator)
            {
                continue;
            }

            var layers = tracks
                .Select(track => ReadLayer(animator, match.Animator.Path, track, cycle))
                .OfType<ParallaxLayer>()
                .OrderBy(candidate => candidate.ZIndex)
                .ToList();
            backgrounds.Add(new ParallaxBackground(match.Path, animationPath, match.Animator.Path, String(animator["name"]) ?? "", Label(match.Root, match.Animator.Path), cycle, layers));
        }

        return backgrounds;
    }

    public string AddLayer(string themeDraftPath, string? baseUiPath, ParallaxBackground background, NewParallaxLayer layer)
    {
        ArgumentNullException.ThrowIfNull(background);
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentException.ThrowIfNullOrWhiteSpace(layer.Name);
        if (layer.Scale is < 1 or > 8)
        {
            throw new ArgumentException("The scale must be between 1 and 8.", nameof(layer));
        }

        var (themeLayer, scene, root) = OpenScene(themeDraftPath, baseUiPath, background.SceneResourcePath);
        var animator = Animator(root, background);
        var children = Children(animator);
        var name = UniqueName(children, layer.Name.Trim());
        var (animationFile, animation) = OpenAnimation(themeLayer, background);

        // the image is repeated in the PNG itself until it covers the screen, so the layer scrolls without a seam
        // and its texture offset counts whole layer widths, as with the console's own layers
        string textureId;
        int width, height;
        var manifest = DependencyManifest.Load(themeLayer);
        var tiledImage = Path.Combine(Path.GetTempPath(), $"mts-layer-{Guid.NewGuid():N}.png");
        try
        {
            (width, height) = WriteTiledImage(layer.SourceImagePath, layer.Scale, tiledImage);
            var resourcePath = ThemeTextures.FreeResourcePath(themeLayer, manifest, WallpaperFolder, SafeFileName(name));
            textureId = ThemeTextures.Register(themeLayer, manifest, resourcePath, tiledImage);
        }
        finally
        {
            File.Delete(tiledImage);
        }

        var node = ImageNode(name, layer.ZIndex, textureId, width * layer.Scale, height * layer.Scale);
        node["transform"] = new JsonArray(1, 0, 0, 0, 1, Number(layer.Y));
        children.Add(node);
        (animation["animations"] as JsonArray ?? throw new InvalidDataException($"{Path.GetFileName(animationFile)} has no animations list."))
            .Add(new JsonObject
            {
                ["worldNodePath"] = new JsonArray(name),
                ["type"] = "float",
                ["componentType"] = "TextureComponent",
                ["propertyId"] = OffsetProperty,
                ["keyCurves"] = Keys(0, layer.LoopsPerCycle, background.CycleSeconds),
            });

        manifest.ChangeReferenceCount(DependencyManifest.Scene, background.SceneResourcePath, textureId, 1);
        manifest.Save(themeLayer);
        themeLayer.WriteThemeResource(background.SceneResourcePath, EngineJson.ToCompactString(scene));
        themeLayer.WriteThemeResource(background.AnimationResourcePath, EngineJson.ToCompactString(animation));
        return ChildPath(background.AnimatorPath, children.Count - 1);
    }

    public void SetSpeed(string themeDraftPath, string? baseUiPath, ParallaxBackground background, string layerName, int loopsPerCycle)
    {
        ArgumentNullException.ThrowIfNull(background);
        var themeLayer = new ThemeLayer(themeDraftPath, baseUiPath);
        var (_, animation) = OpenAnimation(themeLayer, background);
        var track = OffsetTracks(animation).FirstOrDefault(candidate => TrackName(candidate) == layerName)
            ?? throw new InvalidDataException($"{layerName} does not scroll in {Path.GetFileName(background.AnimationResourcePath)}.");

        var start = (track["keyCurves"] as JsonArray)?.FirstOrDefault() is JsonObject first ? KeyStart(first) : 0;
        track["keyCurves"] = Keys(start, loopsPerCycle, background.CycleSeconds);
        themeLayer.WriteThemeResource(background.AnimationResourcePath, EngineJson.ToCompactString(animation));
    }

    public void RemoveLayer(string themeDraftPath, string? baseUiPath, ParallaxBackground background, string layerName)
    {
        ArgumentNullException.ThrowIfNull(background);
        var layer = background.Layers.FirstOrDefault(candidate => candidate.Name == layerName)
            ?? throw new InvalidDataException($"{layerName} is not a layer of {background.AnimatorName}.");

        var textureId = sceneEditor.Load(themeDraftPath, baseUiPath, background.SceneResourcePath).Find(layer.NodePath)?.Visual?.TextureId;

        // the node goes first: it is kept when the scene still links to it, and then the track stays too
        sceneEditor.DeleteNode(themeDraftPath, baseUiPath, background.SceneResourcePath, layer.NodePath);

        var themeLayer = new ThemeLayer(themeDraftPath, baseUiPath);
        var (_, animation) = OpenAnimation(themeLayer, background);
        var tracks = animation["animations"] as JsonArray ?? [];
        foreach (var track in OffsetTracks(animation).Where(candidate => TrackName(candidate) == layerName).ToList())
        {
            tracks.Remove(track);
        }

        themeLayer.WriteThemeResource(background.AnimationResourcePath, EngineJson.ToCompactString(animation));

        // the image of a layer the theme added goes too, unless another node still draws it
        if (textureId is not null
            && DependencyManifest.TryLoad(themeLayer) is { } manifest
            && manifest.FindById(textureId) is { Path: { } imagePath }
            && themeLayer.ResolveInTheme(imagePath) is not null
            && themeLayer.ResolveInBase(imagePath) is null
            && manifest.UsersOf(textureId).Count == 0)
        {
            ThemeTextures.Delete(themeLayer, imagePath);
        }
    }

    private static ParallaxLayer? ReadLayer(JsonObject animator, string animatorPath, JsonObject track, double cycle)
    {
        var name = TrackName(track);
        if (Child(animator, name) is not ({ } node, var index))
        {
            return null;
        }

        var component = Components(node).FirstOrDefault(candidate => String(candidate["type"]) == "TextureComponent");
        var width = component?["size"] is JsonArray { Count: 2 } size ? Double(size[0]) ?? 0 : 0;
        var repeat = component?["uvRepeat"] is JsonArray { Count: 2 } uvRepeat ? Double(uvRepeat[0]) ?? 1 : 1;
        var keys = (track["keyCurves"] as JsonArray ?? []).OfType<JsonObject>().ToList();
        var loops = keys.Count == 0 ? 0 : KeyEnd(keys[^1]) - KeyStart(keys[0]);
        var y = node["transform"] is JsonArray { Count: 6 } transform ? Double(transform[5]) ?? 0 : 0;
        return new ParallaxLayer(
            name,
            ChildPath(animatorPath, index),
            y,
            (int)Math.Round(Double(node["zIndex"]) ?? 0),
            repeat > 0 ? width / repeat : width,
            loops,
            cycle);
    }

    // a straight slide over the cycle: hermite keys whose tangents are the slope of the line
    private static JsonArray Keys(double start, int loops, double cycle)
    {
        var slope = cycle > 0 ? loops / cycle : 0;
        return new JsonArray(
            new JsonObject { ["time"] = 0, ["interp"] = "hermite", ["value"] = new JsonArray(Number(start), Number(start + loops), slope, slope) },
            new JsonObject { ["time"] = cycle, ["value"] = Number(start + loops) });
    }

    // a key's value is a number, or [from, to, tangent out, tangent in] when it starts a curve
    private static double KeyStart(JsonObject key) =>
        key["value"] is JsonArray { Count: > 0 } curve ? Double(curve[0]) ?? 0 : Double(key["value"]) ?? 0;

    private static double KeyEnd(JsonObject key) =>
        key["value"] is JsonArray { Count: > 1 } curve ? Double(curve[1]) ?? 0 : Double(key["value"]) ?? 0;

    private static IEnumerable<JsonObject> OffsetTracks(JsonObject animation)
    {
        return (animation["animations"] as JsonArray ?? []).OfType<JsonObject>()
            .Where(track => String(track["propertyId"]) == OffsetProperty
                && String(track["componentType"]) == "TextureComponent"
                && track["worldNodePath"] is JsonArray { Count: 1 });
    }

    private static string TrackName(JsonObject track) => String((track["worldNodePath"] as JsonArray)?[0]) ?? "";

    private static (JsonObject Node, int Index)? Child(JsonObject parent, string name)
    {
        var children = (parent["children"] as JsonArray ?? []).ToList();
        var index = children.FindIndex(child => child is JsonObject node && String(node["name"]) == name);
        return index < 0 ? null : ((JsonObject)children[index]!, index);
    }

    private static IEnumerable<(JsonObject Node, string Path)> IndexedNodes(JsonObject node, string path)
    {
        yield return (node, path);
        var children = (node["children"] as JsonArray ?? []).ToList();
        for (var index = 0; index < children.Count; index++)
        {
            if (children[index] is JsonObject child)
            {
                foreach (var descendant in IndexedNodes(child, ChildPath(path, index)))
                {
                    yield return descendant;
                }
            }
        }
    }

    // the names from the root's child down to the node, which tell apart animators that share a name
    private static string Label(JsonObject root, string path)
    {
        var names = new List<string>();
        var node = root;
        foreach (var part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            node = FindNode(node, part);
            names.Add(String(node?["name"]) ?? "?");
        }

        return names.Count == 0 ? String(root["name"]) ?? "" : string.Join("/", names);
    }

    private static JsonObject Animator(JsonObject root, ParallaxBackground background)
    {
        var animator = FindNode(root, background.AnimatorPath);
        return animator is not null && String(animator["name"]) == background.AnimatorName
            ? animator
            : throw new InvalidDataException($"{background.AnimatorName} is no longer in {Path.GetFileName(background.SceneResourcePath)}.");
    }

    private static (string File, JsonObject Animation) OpenAnimation(ThemeLayer layer, ParallaxBackground background)
    {
        var file = layer.Resolve(background.AnimationResourcePath)
            ?? throw new FileNotFoundException($"Animation not found: resources{background.AnimationResourcePath}");
        return (file, ParseScene(file));
    }

    // the image side by side as many times as the screen needs; returns the size of the written PNG
    private static (int Width, int Height) WriteTiledImage(string sourceImagePath, int scale, string targetPath)
    {
        using var source = SKBitmap.Decode(sourceImagePath)
            ?? throw new InvalidDataException($"{Path.GetFileName(sourceImagePath)} is not a valid PNG image.");
        var copies = Math.Max(1, (int)Math.Ceiling(ScreenWidth / (source.Width * scale)));
        using var tiled = new SKBitmap(new SKImageInfo(source.Width * copies, source.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        using (var canvas = new SKCanvas(tiled))
        {
            canvas.Clear(SKColors.Transparent);
            for (var copy = 0; copy < copies; copy++)
            {
                canvas.DrawBitmap(source, copy * source.Width, 0);
            }
        }

        using var png = tiled.Encode(SKEncodedImageFormat.Png, 100);
        using (var file = File.Create(targetPath))
        {
            png.SaveTo(file);
        }

        return (tiled.Width, tiled.Height);
    }
}
