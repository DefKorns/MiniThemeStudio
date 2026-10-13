using System.Globalization;
using System.Text;

namespace MiniThemeStudio.Core.Themes;

public static class ThemeDirectoryName
{
    public static string FromDisplayName(string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        var normalizedName = displayName.Normalize(NormalizationForm.FormD);
        var result = new StringBuilder(normalizedName.Length);
        var separatorPending = false;

        foreach (var character in normalizedName)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                if (separatorPending && result.Length > 0)
                {
                    result.Append('_');
                }

                result.Append(char.ToLowerInvariant(character));
                separatorPending = false;
            }
            else
            {
                separatorPending = result.Length > 0;
            }
        }

        if (result.Length == 0)
        {
            throw new ArgumentException(
                "The theme name must contain at least one letter or number.",
                nameof(displayName));
        }

        return result.ToString();
    }
}
