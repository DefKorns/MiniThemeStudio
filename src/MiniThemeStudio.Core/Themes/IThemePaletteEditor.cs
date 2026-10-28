namespace MiniThemeStudio.Core.Themes;

public interface IThemePaletteEditor
{
    IReadOnlyList<ThemePalette> GetPalettes(string themeDraftPath, string? baseUiPath = null);

    IReadOnlyList<PaletteColorUsage> GetUsages(string themeDraftPath, string? baseUiPath = null);

    /// <summary>Writes the colour into the draft's copy of each palette, copying it from the base first if needed.</summary>
    void SetColor(string themeDraftPath, string? baseUiPath, PaletteColor color, IEnumerable<string> relativePaths);
}
