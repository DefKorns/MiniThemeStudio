namespace MiniThemeStudio.Core.Themes;

public static class ThemeMetadataResolver
{
    public static string ResolveName(
        string directoryName,
        string? title,
        IEnumerable<string> infoLines)
    {
        if (!string.IsNullOrWhiteSpace(title))
        {
            return title.Trim();
        }

        const string namePrefix = "NAME=";
        string? infoName = infoLines
            .FirstOrDefault(line => line.StartsWith(namePrefix, StringComparison.OrdinalIgnoreCase));

        return string.IsNullOrWhiteSpace(infoName)
            ? directoryName
            : infoName[namePrefix.Length..].Trim();
    }
}
