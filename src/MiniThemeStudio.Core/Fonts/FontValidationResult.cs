using System.Collections.Generic;

namespace MiniThemeStudio.Core.Fonts;

public sealed record FontValidationResult(
    bool HasBasicAscii,
    bool HasLatinAccents,
    IReadOnlyList<char> MissingAsciiChars,
    IReadOnlyList<char> MissingAccentChars,
    int TotalGlyphs)
{
    public bool IsSafeForWesternUi => HasBasicAscii && HasLatinAccents;

    public string Summary => IsSafeForWesternUi
        ? $"Full Western coverage ({TotalGlyphs} glyphs, ASCII + European accents present)."
        : !HasBasicAscii
            ? $"Missing basic ASCII characters ({MissingAsciiChars.Count} missing). High risk of UI corruption."
            : $"Missing European accents ({MissingAccentChars.Count} missing, e.g. {string.Join(", ", MissingAccentChars.Take(8))}). Potential C8 error on translated UI.";
}
