using System.Collections.Generic;
using System.IO;

namespace MiniThemeStudio.Core.Fonts;

public sealed record TrueTypeRasterOptions(
    int FontSize = 16,
    bool AntiAlias = false,
    int AtlasWidth = 512,
    IReadOnlyCollection<char>? CharacterSet = null,
    MiniFont? FallbackFont = null,
    ExtraScripts ExtraScripts = ExtraScripts.None,
    /// <summary>
    /// Font family used for extra-script glyphs not present in the custom font.
    /// Use null for system auto-match, or a specific family such as
    /// "Yu Gothic UI" (Windows) or "Noto Sans CJK JP" / "Noto Sans" (cross-platform).
    /// </summary>
    string? ScriptFallbackFamily = null);




public interface ITrueTypeFontRasterizer
{
    MiniFont Rasterize(Stream fontStream, TrueTypeRasterOptions options, string? fontName = null);
    MiniFont Rasterize(string fontOrZipPath, TrueTypeRasterOptions options);
}
