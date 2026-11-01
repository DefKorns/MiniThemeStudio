namespace MiniThemeStudio.Infrastructure.Fonts;

// A theme keeps its fonts in a fonts folder beside resources, named as Font Remount (the hakchi mod that mounts
// them over the console's) reads them: title, copyright, small, medium and large. The UIs name the last three
// s, m and l, and keep them in resources/fonts, or in a folder of the console on the NES and Famicom.
internal static class ThemeFonts
{
    public const string Folder = "fonts";

    private static readonly Dictionary<string, string> ThemeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["s"] = "small",
        ["m"] = "medium",
        ["l"] = "large",
    };

    // the name a font of the UI has in a theme: l is large
    public static string ThemeName(string uiName) =>
        ThemeNames.TryGetValue(uiName, out string? themeName) ? themeName : uiName.ToLowerInvariant();

    // the name of the UI's font a theme font replaces: large is l
    public static string UiName(string themeName) =>
        ThemeNames.FirstOrDefault(pair => pair.Value.Equals(themeName, StringComparison.OrdinalIgnoreCase)).Key ?? themeName.ToLowerInvariant();

    // where the theme keeps the font, relative to its folder: fonts/large.fnt
    public static string RelativePath(string uiName, string extension) => $"{Folder}/{ThemeName(uiName)}{extension}";

    public static bool IsFontFile(string path)
    {
        string extension = Path.GetExtension(path);
        return extension.Equals(".fnt", StringComparison.OrdinalIgnoreCase) || extension.Equals(".font", StringComparison.OrdinalIgnoreCase);
    }
}
