using System.Text.Json;
using MiniThemeStudio.Core.Themes;

namespace MiniThemeStudio.Infrastructure.Themes;

/// <summary>
/// The NES Base ships only colors_nes.lua and the Famicom Base only colors_hvc.lua, yet a theme carries both.
/// The two palettes have the same colours and differ in six values, so either is generated from the other.
/// </summary>
internal static class NesFamilyPalettes
{
    public const string NesFileName = "colors_nes.lua";
    public const string FamicomFileName = "colors_hvc.lua";

    private static readonly PaletteColor[] NesColors =
    [
        PaletteColor.FromBytes("bg_base", 0, 21, 61, 255),
        PaletteColor.FromBytes("font_home_gametitle", 255, 255, 255, 255),
        PaletteColor.FromBytes("font_home_hud", 219, 43, 0, 255),
        PaletteColor.FromBytes("font_resume_hud", 219, 43, 0, 255),
        PaletteColor.FromBytes("font_resume_time", 0, 0, 0, 255),
        PaletteColor.FromBytes("font_resume_title", 219, 43, 0, 255),
    ];

    private static readonly PaletteColor[] FamicomColors =
    [
        PaletteColor.FromBytes("bg_base", 235, 226, 213, 255),
        PaletteColor.FromBytes("font_home_gametitle", 0, 0, 0, 255),
        PaletteColor.FromBytes("font_home_hud", 167, 0, 0, 255),
        PaletteColor.FromBytes("font_resume_hud", 0, 0, 0, 255),
        PaletteColor.FromBytes("font_resume_time", 255, 255, 255, 255),
        PaletteColor.FromBytes("font_resume_title", 255, 255, 255, 255),
    ];

    /// <summary>The palette the Base lacks, as a resource path, when the Base is a NES or a Famicom one.</summary>
    public static string? MissingPaletteResource(ThemeLayer layer, bool inBaseOnly = false)
    {
        if (layer.BaseUiPath is null || IsShonen(layer.BaseUiPath))
        {
            return null;
        }

        string? nes = Find(layer, NesFileName, inBaseOnly);
        string? famicom = Find(layer, FamicomFileName, inBaseOnly);
        return (nes, famicom) switch
        {
            (not null, null) => nes.Replace(NesFileName, FamicomFileName, StringComparison.Ordinal),
            (null, not null) => famicom.Replace(FamicomFileName, NesFileName, StringComparison.Ordinal),
            _ => null,
        };
    }

    // inBaseOnly: the Base's own palettes, whatever the theme holds
    private static string? Find(ThemeLayer layer, string fileName, bool inBaseOnly)
    {
        List<LayeredFile> found = layer.ListFiles("scripts", fileName)
            .Where(file => !inBaseOnly || file.IsFromBase || file.OverridesBase)
            .ToList();
        return found.Count > 0 ? found[0].ResourcePath : null;
    }

    /// <summary>The bytes of the missing palette, made from the one the theme has.</summary>
    public static byte[] Generate(ThemeLayer layer, string missingResourcePath, bool fromBaseOnly = false)
    {
        bool toFamicom = missingResourcePath.EndsWith(FamicomFileName, StringComparison.Ordinal);
        string sourceResource = missingResourcePath.Replace(toFamicom ? FamicomFileName : NesFileName, toFamicom ? NesFileName : FamicomFileName, StringComparison.Ordinal);
        var palette = LuaColorPalette.Read((fromBaseOnly ? layer.ResolveInBase(sourceResource) : layer.Resolve(sourceResource)) ?? throw new FileNotFoundException($"Neither the theme nor its base has {sourceResource}."));
        HashSet<string> present = palette.Colors.Select(color => color.Name).ToHashSet();
        foreach (PaletteColor color in (toFamicom ? FamicomColors : NesColors).Where(color => present.Contains(color.Name)))
        {
            palette.SetColor(color);
        }

        return palette.ToBytes();
    }

    // the Shonen Jump firmware has a colors_hvc.lua of its own and no NES counterpart
    private static bool IsShonen(string baseUiPath)
    {
        string initPath = Path.Combine(baseUiPath, "init.json");
        if (!File.Exists(initPath))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(initPath));
            return document.RootElement.TryGetProperty("engineFormat", out JsonElement format) && format.GetInt32() >= 10;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or IOException)
        {
            return false;
        }
    }
}
