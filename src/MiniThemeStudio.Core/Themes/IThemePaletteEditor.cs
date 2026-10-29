namespace MiniThemeStudio.Core.Themes;

public interface IThemePaletteEditor
{
    IReadOnlyList<ThemePalette> GetPalettes(string themeDraftPath, string? baseUiPath = null);

    IReadOnlyList<PaletteColorUsage> GetUsages(string themeDraftPath, string? baseUiPath = null);

    /// <summary>The palettes as the Base has them, whatever the draft changed.</summary>
    IReadOnlyList<ThemePalette> GetDefaultPalettes(string themeDraftPath, string? baseUiPath = null);

    /// <summary>
    /// Puts the colours (every colour when null) back to the Base's values in each palette. A palette left as the
    /// Base's is removed from the draft, so the package no longer carries it.
    /// </summary>
    void RestoreDefaults(string themeDraftPath, string? baseUiPath, IEnumerable<string>? colorNames);

    /// <summary>Writes the colour into the draft's copy of each palette, copying it from the base first if needed.</summary>
    void SetColor(string themeDraftPath, string? baseUiPath, PaletteColor color, IEnumerable<string> relativePaths);
}
