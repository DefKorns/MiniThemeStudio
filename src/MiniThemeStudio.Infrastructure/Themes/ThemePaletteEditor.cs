using System.Text.Json.Nodes;
using MiniThemeStudio.Core.Themes;

namespace MiniThemeStudio.Infrastructure.Themes;

public sealed class ThemePaletteEditor : IThemePaletteEditor
{
    private const string ScriptsFolder = "scripts";
    private const string ResourcesFolder = "resources";

    public IReadOnlyList<ThemePalette> GetPalettes(string themeDraftPath, string? baseUiPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themeDraftPath);

        var layer = new ThemeLayer(themeDraftPath, baseUiPath);
        var palettes = new List<ThemePalette>();
        foreach (var relativePath in RelativePaths(layer, ScriptsFolder, "colors_*.lua"))
        {
            var (filePath, fromBase) = Resolve(layer, relativePath);
            var region = Path.GetFileNameWithoutExtension(relativePath)["colors_".Length..];
            palettes.Add(new ThemePalette(region, relativePath, filePath, fromBase, LuaColorPalette.Read(filePath).Colors));
        }

        return palettes;
    }

    public IReadOnlyList<PaletteColorUsage> GetUsages(string themeDraftPath, string? baseUiPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themeDraftPath);

        var layer = new ThemeLayer(themeDraftPath, baseUiPath);
        var usages = new List<PaletteColorUsage>();
        foreach (var scene in RelativePaths(layer, "", "*.scn"))
        {
            var (filePath, _) = Resolve(layer, scene);
            if (JsonNode.Parse(File.ReadAllText(filePath))?["rootWorldNode"] is JsonObject root)
            {
                CollectUsages(root, "", scene, usages);
            }
        }

        // LuaJIT bytecode keeps the strings a script uses as plain text, so a colour named in a script shows up as bytes
        var names = GetPalettes(themeDraftPath, baseUiPath).SelectMany(p => p.Colors).Select(c => c.Name).Distinct().ToList();
        foreach (var script in RelativePaths(layer, ScriptsFolder, "*.lua")
            .Where(path => !Path.GetFileName(path).StartsWith("colors_", StringComparison.OrdinalIgnoreCase)))
        {
            var (filePath, _) = Resolve(layer, script);
            var bytes = File.ReadAllBytes(filePath);
            foreach (var name in names.Where(name => bytes.AsSpan().IndexOf(System.Text.Encoding.UTF8.GetBytes(name)) >= 0))
            {
                usages.Add(new PaletteColorUsage(name, script, "(Lua script)", null));
            }
        }

        return usages;
    }

    public void SetColor(string themeDraftPath, string? baseUiPath, PaletteColor color, IEnumerable<string> relativePaths)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themeDraftPath);
        ArgumentNullException.ThrowIfNull(relativePaths);

        var layer = new ThemeLayer(themeDraftPath, baseUiPath);
        foreach (var relativePath in relativePaths)
        {
            var (sourcePath, _) = Resolve(layer, relativePath);
            var palette = LuaColorPalette.Read(sourcePath);
            palette.SetColor(color);

            var targetPath = layer.ThemeResourceFile(ToResourcePath(relativePath));
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            palette.Write(targetPath);
        }
    }

    // palettes and usages name files relative to the theme root ("resources/scripts/colors_usa.lua")
    private static IEnumerable<string> RelativePaths(ThemeLayer layer, string folder, string pattern) =>
        layer.EnumerateResources(folder, pattern).Select(resourcePath => ResourcesFolder + resourcePath);

    private static (string FilePath, bool FromBase) Resolve(ThemeLayer layer, string relativePath)
    {
        var resourcePath = ToResourcePath(relativePath);
        if (layer.ResolveInTheme(resourcePath) is { } themeFile)
        {
            return (themeFile, false);
        }

        return layer.ResolveInBase(resourcePath) is { } baseFile
            ? (baseFile, true)
            : throw new FileNotFoundException($"Neither the theme nor its base has {relativePath}.");
    }

    private static string ToResourcePath(string relativePath)
    {
        var path = relativePath.Replace('\\', '/');
        return path.StartsWith(ResourcesFolder + "/", StringComparison.OrdinalIgnoreCase)
            ? path[ResourcesFolder.Length..]
            : throw new ArgumentException($"Expected a path inside {ResourcesFolder}/: {relativePath}", nameof(relativePath));
    }

    private static void CollectUsages(JsonObject node, string parentPath, string scene, List<PaletteColorUsage> usages)
    {
        var path = $"{parentPath}/{node["name"]?.GetValue<string>()}";
        foreach (var component in node["components"]?.AsArray().OfType<JsonObject>() ?? [])
        {
            if (component["properties"]?["colorName"]?.GetValue<string>() is { Length: > 0 } colorName)
            {
                var text = component["text"]?.GetValue<string>();
                usages.Add(new PaletteColorUsage(colorName, scene, path, string.IsNullOrEmpty(text) ? null : text));
            }
        }

        foreach (var child in node["children"]?.AsArray().OfType<JsonObject>() ?? [])
        {
            CollectUsages(child, path, scene, usages);
        }
    }
}
