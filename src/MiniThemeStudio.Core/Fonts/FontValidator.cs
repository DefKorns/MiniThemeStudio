namespace MiniThemeStudio.Core.Fonts;

public static class FontValidator
{
    private static readonly char[] AlphaNumericChars =
    [
        'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'J', 'K', 'L', 'M',
        'N', 'O', 'P', 'Q', 'R', 'S', 'T', 'U', 'V', 'W', 'X', 'Y', 'Z',
        'a', 'b', 'c', 'd', 'e', 'f', 'g', 'h', 'i', 'j', 'k', 'l', 'm',
        'n', 'o', 'p', 'q', 'r', 's', 't', 'u', 'v', 'w', 'x', 'y', 'z',
        '0', '1', '2', '3', '4', '5', '6', '7', '8', '9'
    ];

    private static readonly char[] EssentialAccents =
    [
        'á', 'à', 'â', 'ã', 'ä', 'é', 'è', 'ê', 'ë', 'í', 'ì', 'î', 'ï',
        'ó', 'ò', 'ô', 'õ', 'ö', 'ú', 'ù', 'û', 'ü', 'ç', 'ñ',
        'Á', 'À', 'Â', 'Ã', 'Ä', 'É', 'È', 'Ê', 'Ë', 'Í', 'Ì', 'Î', 'Ï',
        'Ó', 'Ò', 'Ô', 'Õ', 'Ö', 'Ú', 'Ù', 'Û', 'Ü', 'Ç', 'Ñ'
    ];

    public static FontValidationResult Validate(MiniFont font)
    {
        var missingAlphaNumeric = new List<char>();
        foreach (var c in AlphaNumericChars)
        {
            if (!font.HasGlyph(c))
            {
                missingAlphaNumeric.Add(c);
            }
        }

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
            HasAlphaNumeric: missingAlphaNumeric.Count == 0,
            HasBasicAscii: missingAscii.Count == 0,
            HasLatinAccents: missingAccents.Count == 0,
            MissingAlphaNumericChars: missingAlphaNumeric,
            MissingAsciiChars: missingAscii,
            MissingAccentChars: missingAccents,
            TotalGlyphs: font.Glyphs.Count);
    }
}
