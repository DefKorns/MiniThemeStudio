using System.Collections.Generic;

namespace MiniThemeStudio.Core.Fonts;

public static class FontValidator
{
    private static readonly char[] EssentialAccents =
    [
        'á', 'à', 'â', 'ã', 'ä', 'é', 'è', 'ê', 'ë', 'í', 'ì', 'î', 'ï',
        'ó', 'ò', 'ô', 'õ', 'ö', 'ú', 'ù', 'û', 'ü', 'ç', 'ñ',
        'Á', 'À', 'Â', 'Ã', 'Ä', 'É', 'È', 'Ê', 'Ë', 'Í', 'Ì', 'Î', 'Ï',
        'Ó', 'Ò', 'Ô', 'Õ', 'Ö', 'Ú', 'Ù', 'Û', 'Ü', 'Ç', 'Ñ'
    ];

    public static FontValidationResult Validate(MiniFont font)
    {
        var missingAscii = new List<char>();
        for (var c = ' '; c <= '~'; c++)
        {
            if (!font.HasGlyph(c))
            {
                missingAscii.Add(c);
            }
        }

        var missingAccents = new List<char>();
        foreach (var c in EssentialAccents)
        {
            if (!font.HasGlyph(c))
            {
                missingAccents.Add(c);
            }
        }

        return new FontValidationResult(
            HasBasicAscii: missingAscii.Count == 0,
            HasLatinAccents: missingAccents.Count == 0,
            MissingAsciiChars: missingAscii,
            MissingAccentChars: missingAccents,
            TotalGlyphs: font.Glyphs.Count);
    }
}
