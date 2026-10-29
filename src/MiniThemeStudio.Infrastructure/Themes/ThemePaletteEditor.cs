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
        foreach (string relativePath in RelativePaths(layer, ScriptsFolder, "colors_*.lua"))
        {
            (string? filePath, bool fromBase) = Resolve(layer, relativePath);
            string region = Path.GetFileNameWithoutExtension(relativePath)["colors_".Length..];
            palettes.Add(new ThemePalette(region, relativePath, filePath, fromBase, LuaColorPalette.Read(filePath).Colors));
        }

        // the palette of the other NES-family console, which the Base lacks, is generated (FilePath is empty until it is written)
        if (NesFamilyPalettes.MissingPaletteResource(layer) is { } missing)
        {
            string relativePath = ResourcesFolder + missing;
            string region = Path.GetFileNameWithoutExtension(relativePath)["colors_".Length..];
            palettes.Add(new ThemePalette(region, relativePath, "", true, LuaColorPalette.Read(NesFamilyPalettes.Generate(layer, missing)).Colors));
        }

        return palettes;
    }

    public IReadOnlyList<PaletteColorUsage> GetUsages(string themeDraftPath, string? baseUiPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themeDraftPath);

        var layer = new ThemeLayer(themeDraftPath, baseUiPath);
        var usages = new List<PaletteColorUsage>();
        foreach (string scene in RelativePaths(layer, "", "*.scn"))
        {
            (string? filePath, bool _) = Resolve(layer, scene);
            if (JsonNode.Parse(File.ReadAllText(filePath))?["rootWorldNode"] is JsonObject root)
            {
                CollectUsages(root, "", scene, usages);
            }
        }

        // LuaJIT bytecode keeps the strings a script uses as plain text, so a colour named in a script shows up as bytes
        var names = GetPalettes(themeDraftPath, baseUiPath).SelectMany(p => p.Colors).Select(c => c.Name).Distinct().ToList();
        foreach (string? script in RelativePaths(layer, ScriptsFolder, "*.lua")
            .Where(path => !Path.GetFileName(path).StartsWith("colors_", StringComparison.OrdinalIgnoreCase)))
        {
            (string? filePath, bool _) = Resolve(layer, script);
            byte[] bytes = File.ReadAllBytes(filePath);
            foreach (string? name in names.Where(name => bytes.AsSpan().IndexOf(System.Text.Encoding.UTF8.GetBytes(name)) >= 0))
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
        foreach (string relativePath in relativePaths)
        {
            LuaColorPalette palette = ReadForEditing(layer, relativePath);
            palette.SetColor(color);

            string targetPath = layer.ThemeResourceFile(ToResourcePath(relativePath));
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            palette.Write(targetPath);
        }
    }

    // palettes and usages name files relative to the theme root ("resources/scripts/colors_usa.lua")
    private static IEnumerable<string> RelativePaths(ThemeLayer layer, string folder, string pattern) =>
        layer.EnumerateResources(folder, pattern).Select(resourcePath => ResourcesFolder + resourcePath);

    private static LuaColorPalette ReadForEditing(ThemeLayer layer, string relativePath)
    {
        string resourcePath = ToResourcePath(relativePath);
        return layer.Resolve(resourcePath) is { } existing
            ? LuaColorPalette.Read(existing)
            : NesFamilyPalettes.MissingPaletteResource(layer) == resourcePath
                ? LuaColorPalette.Read(NesFamilyPalettes.Generate(layer, resourcePath))
                : throw new FileNotFoundException($"Neither the theme nor its base has {relativePath}.");
    }

    private static (string FilePath, bool FromBase) Resolve(ThemeLayer layer, string relativePath)
    {
        string resourcePath = ToResourcePath(relativePath);
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
        string path = relativePath.Replace('\\', '/');
        return path.StartsWith(ResourcesFolder + "/", StringComparison.OrdinalIgnoreCase)
            ? path[ResourcesFolder.Length..]
            : throw new ArgumentException($"Expected a path inside {ResourcesFolder}/: {relativePath}", nameof(relativePath));
    }

    private static void CollectUsages(JsonObject node, string parentPath, string scene, List<PaletteColorUsage> usages)
    {
        string path = $"{parentPath}/{node["name"]?.GetValue<string>()}";
        foreach (JsonObject component in node["components"]?.AsArray().OfType<JsonObject>() ?? [])
        {
            if (component["properties"]?["colorName"]?.GetValue<string>() is { Length: > 0 } colorName)
            {
                string? text = component["text"]?.GetValue<string>();
                usages.Add(new PaletteColorUsage(colorName, scene, path, string.IsNullOrEmpty(text) ? null : text));
            }
        }

        foreach (JsonObject child in node["children"]?.AsArray().OfType<JsonObject>() ?? [])
        {
            CollectUsages(child, path, scene, usages);
        }
    }
}
