namespace MiniThemeStudio.Core.Fonts;

public sealed record FontValidationResult(
    bool HasAlphaNumeric,
    bool HasBasicAscii,
    bool HasLatinAccents,
    IReadOnlyList<char> MissingAlphaNumericChars,
    IReadOnlyList<char> MissingAsciiChars,
    IReadOnlyList<char> MissingAccentChars,
    int TotalGlyphs)
{
    public bool IsSafeForWesternUi => HasBasicAscii && HasLatinAccents;

    public string Summary => IsSafeForWesternUi
        ? $"Full Western coverage ({TotalGlyphs} glyphs, ASCII + European accents present)."
        : !HasAlphaNumeric
            ? $"Missing alphanumeric characters ({MissingAlphaNumericChars.Count} missing, e.g. {string.Join(", ", MissingAlphaNumericChars.Take(6))}). High risk of UI corruption."
            : MissingAccentChars.Count > 0
                ? $"Missing European accents ({MissingAccentChars.Count} missing, e.g. {string.Join(", ", MissingAccentChars.Take(6))}). Potential C8 error on translated UI."
                : $"Missing {MissingAsciiChars.Count} secondary symbols ({string.Join(" ", MissingAsciiChars.Take(8))}).";
}
