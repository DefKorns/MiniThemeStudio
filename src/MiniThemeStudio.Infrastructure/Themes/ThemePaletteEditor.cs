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

    public IReadOnlyList<ThemePalette> GetDefaultPalettes(string themeDraftPath, string? baseUiPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themeDraftPath);

        var layer = new ThemeLayer(themeDraftPath, baseUiPath);
        var palettes = new List<ThemePalette>();
        foreach (ThemePalette palette in GetPalettes(themeDraftPath, baseUiPath))
        {
            if (DefaultBytes(layer, ToResourcePath(palette.RelativePath)) is { } bytes)
            {
                palettes.Add(palette with { FilePath = "", IsFromBaseUi = true, Colors = LuaColorPalette.Read(bytes).Colors });
            }
        }

        return palettes;
    }

    public void RestoreDefaults(string themeDraftPath, string? baseUiPath, IEnumerable<string>? colorNames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themeDraftPath);

        var layer = new ThemeLayer(themeDraftPath, baseUiPath);
        List<string>? names = colorNames?.ToList();
        foreach (ThemePalette palette in GetPalettes(themeDraftPath, baseUiPath))
        {
            string resourcePath = ToResourcePath(palette.RelativePath);
            if (layer.ResolveInTheme(resourcePath) is not { } draftFile || DefaultBytes(layer, resourcePath) is not { } defaultBytes)
            {
                continue;
            }

            var defaults = LuaColorPalette.Read(defaultBytes);
            var edited = LuaColorPalette.Read(draftFile);
            foreach (PaletteColor color in defaults.Colors.Where(color => names is null || names.Contains(color.Name)))
            {
                edited.SetColor(color);
            }

            if (edited.ToBytes().AsSpan().SequenceEqual(defaultBytes))
            {
                File.Delete(draftFile);
            }
            else
            {
                edited.Write(draftFile);
            }
        }
    }

    public IReadOnlyList<PaletteColorUsage> GetUsages(string themeDraftPath, string? baseUiPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themeDraftPath);

        var layer = new ThemeLayer(themeDraftPath, baseUiPath);
        var usages = new List<PaletteColorUsage>();
        IReadOnlyDictionary<string, string> strings = ReadStrings(layer);
        foreach (string scene in RelativePaths(layer, "", "*.scn"))
        {
            (string? filePath, bool _) = Resolve(layer, scene);
            if (JsonNode.Parse(File.ReadAllText(filePath))?["rootWorldNode"] is JsonObject root)
            {
                CollectUsages(root, "", scene, strings, usages);
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

    // the palette as the Base has it: its own file, or the one generated for the other NES-family console
    private static byte[]? DefaultBytes(ThemeLayer layer, string resourcePath)
    {
        if (layer.ResolveInBase(resourcePath) is { } baseFile)
        {
            return File.ReadAllBytes(baseFile);
        }

        return NesFamilyPalettes.MissingPaletteResource(layer, inBaseOnly: true) == resourcePath
            ? NesFamilyPalettes.Generate(layer, resourcePath, fromBaseOnly: true)
            : null;
    }

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

    // the English strings of the Base ("@sys_resume_Title" is "Suspend Point List"), or the first language it has
    private static Dictionary<string, string> ReadStrings(ThemeLayer layer)
    {
        var strings = new Dictionary<string, string>(StringComparer.Ordinal);
        IReadOnlyList<LayeredFile> files = layer.ListFiles("strings", "strings.*");
        LayeredFile? file = files.FirstOrDefault(candidate => candidate.ResourcePath.Contains("/usa_en/", StringComparison.OrdinalIgnoreCase))
            ?? files.FirstOrDefault(candidate => candidate.ResourcePath.Contains("_en/", StringComparison.OrdinalIgnoreCase))
            ?? (files.Count > 0 ? files[0] : null);
        if (file is null)
        {
            return strings;
        }

        try
        {
            if (JsonNode.Parse(File.ReadAllText(file.FilePath))?["strings"] is JsonObject entries)
            {
                foreach ((string key, JsonNode? value) in entries)
                {
                    if (value is JsonValue text && text.TryGetValue(out string? words))
                    {
                        strings[key] = words;
                    }
                }
            }
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or IOException)
        {
        }

        return strings;
    }

    // a key names a string; text that is no key (the "GameTitle" of a sample label) is shown as it is
    private static string? ReadableText(string? text, IReadOnlyDictionary<string, string> strings)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (text.StartsWith('@'))
        {
            return strings.TryGetValue(text[1..], out string? words) ? words : null;
        }

        return text;
    }

    private static void CollectUsages(JsonObject node, string parentPath, string scene, IReadOnlyDictionary<string, string> strings, List<PaletteColorUsage> usages)
    {
        string path = $"{parentPath}/{node["name"]?.GetValue<string>()}";
        foreach (JsonObject component in node["components"]?.AsArray().OfType<JsonObject>() ?? [])
        {
            if (component["properties"]?["colorName"]?.GetValue<string>() is { Length: > 0 } colorName)
            {
                string? text = component["text"]?.GetValue<string>();
                usages.Add(new PaletteColorUsage(colorName, scene, path, string.IsNullOrEmpty(text) ? null : text, ReadableText(text, strings)));
            }
        }

        foreach (JsonObject child in node["children"]?.AsArray().OfType<JsonObject>() ?? [])
        {
            CollectUsages(child, path, scene, strings, usages);
        }
    }
}
