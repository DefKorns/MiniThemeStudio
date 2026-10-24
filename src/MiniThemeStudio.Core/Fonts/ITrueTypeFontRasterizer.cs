using System.Collections.Generic;
using System.IO;

namespace MiniThemeStudio.Core.Fonts;

public sealed record TrueTypeRasterOptions(
    int FontSize = 16,
    bool AntiAlias = false,
    int AtlasWidth = 512,
    IReadOnlyCollection<char>? CharacterSet = null,
    ExtraScripts ExtraScripts = ExtraScripts.None,
    string? FillFontPath = null);




public interface ITrueTypeFontRasterizer
{
    MiniFont Rasterize(Stream fontStream, TrueTypeRasterOptions options, string? fontName = null);
    MiniFont Rasterize(string fontOrZipPath, TrueTypeRasterOptions options);
}
