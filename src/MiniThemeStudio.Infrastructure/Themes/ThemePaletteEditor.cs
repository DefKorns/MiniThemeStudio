using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using MiniThemeStudio.Core.Themes;

namespace MiniThemeStudio.Infrastructure.Themes;

public sealed class ThemePaletteEditor : IThemePaletteEditor
{
    private const string ScriptsFolder = "resources/scripts";
    private const string ResourcesFolder = "resources";

    public IReadOnlyList<ThemePalette> GetPalettes(string themeDraftPath, string? baseUiPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themeDraftPath);

        var palettes = new List<ThemePalette>();
        foreach (var relativePath in RelativePaths(themeDraftPath, baseUiPath, ScriptsFolder, "colors_*.lua"))
        {
            var (filePath, fromBase) = Resolve(themeDraftPath, baseUiPath, relativePath);
            var region = Path.GetFileNameWithoutExtension(relativePath)["colors_".Length..];
            palettes.Add(new ThemePalette(region, relativePath, filePath, fromBase, LuaColorPalette.Read(filePath).Colors));
        }

        return palettes;
    }

    public IReadOnlyList<PaletteColorUsage> GetUsages(string themeDraftPath, string? baseUiPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themeDraftPath);

        var usages = new List<PaletteColorUsage>();
        foreach (var scene in RelativePaths(themeDraftPath, baseUiPath, ResourcesFolder, "*.scn"))
        {
            var (filePath, _) = Resolve(themeDraftPath, baseUiPath, scene);
            if (JsonNode.Parse(File.ReadAllText(filePath))?["rootWorldNode"] is JsonObject root)
            {
                CollectUsages(root, "", scene, usages);
            }
        }

        // LuaJIT bytecode keeps the strings a script uses as plain text, so a colour named in a script shows up as bytes
        var names = GetPalettes(themeDraftPath, baseUiPath).SelectMany(p => p.Colors).Select(c => c.Name).Distinct().ToList();
        foreach (var script in RelativePaths(themeDraftPath, baseUiPath, ScriptsFolder, "*.lua")
            .Where(path => !Path.GetFileName(path).StartsWith("colors_", StringComparison.OrdinalIgnoreCase)))
        {
            var (filePath, _) = Resolve(themeDraftPath, baseUiPath, script);
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

        foreach (var relativePath in relativePaths)
        {
            var (sourcePath, _) = Resolve(themeDraftPath, baseUiPath, relativePath);
            var palette = LuaColorPalette.Read(sourcePath);
            palette.SetColor(color);

            var targetPath = Path.Combine(themeDraftPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            palette.Write(targetPath);
        }
    }

    // the theme's files and the base's, by relative path; the theme's win in Resolve
    private static IEnumerable<string> RelativePaths(string themeDraftPath, string? baseUiPath, string folder, string pattern) =>
        RelativePaths(baseUiPath, folder, pattern)
            .Concat(RelativePaths(themeDraftPath, folder, pattern))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<string> RelativePaths(string? root, string folder, string pattern)
    {
        if (root is null)
        {
            return [];
        }

        var directory = Path.Combine(root, folder);
        return Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, pattern, SearchOption.AllDirectories)
                .Select(file => Path.GetRelativePath(root, file).Replace('\\', '/'))
            : [];
    }

    private static (string FilePath, bool FromBase) Resolve(string themeDraftPath, string? baseUiPath, string relativePath)
    {
        var draftFile = Path.Combine(themeDraftPath, relativePath);
        if (File.Exists(draftFile))
        {
            return (draftFile, false);
        }

        var baseFile = baseUiPath is null ? null : Path.Combine(baseUiPath, relativePath);
        return baseFile is not null && File.Exists(baseFile)
            ? (baseFile, true)
            : throw new FileNotFoundException($"Neither the theme nor its base has {relativePath}.");
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
