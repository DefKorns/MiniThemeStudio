namespace MiniThemeStudio.Core.Themes;

/// <summary>One colors_&lt;region&gt;.lua; IsFromBaseUi when the theme doesn't ship its own and the base's is shown.</summary>
public sealed record ThemePalette(
    string Region,
    string RelativePath,
    string FilePath,
    bool IsFromBaseUi,
    IReadOnlyList<PaletteColor> Colors);
