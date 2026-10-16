namespace MiniThemeStudio.Core.Fonts;

/// <summary>
/// Extra Unicode script blocks to include when rasterizing a TrueType/OpenType font.
/// For characters not present in the custom font, the system font manager is used
/// as fallback (e.g. Noto Sans, Yu Gothic, etc.).
/// </summary>
[Flags]
public enum ExtraScripts
{
    None     = 0,

    /// <summary>Cyrillic block U+0400–U+04FF (Russian, Bulgarian, Serbian, etc.)</summary>
    Cyrillic = 1 << 0,

    /// <summary>Greek and Coptic block U+0370–U+03FF</summary>
    Greek    = 1 << 1,

    /// <summary>Hiragana + Katakana + 2136 Joyo Kanji</summary>
    Japanese = 1 << 2,
}
