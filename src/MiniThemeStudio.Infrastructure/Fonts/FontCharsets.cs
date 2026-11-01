using MiniThemeStudio.Core.Fonts;

namespace MiniThemeStudio.Infrastructure.Fonts;

/// <summary>
/// Character sets taken from the glyphs the consoles' UIs use (Options Menu - Console
/// Language fonts, stock Japanese firmware fonts and the Japanese UI strings).
/// </summary>
public static class FontCharsets
{
    private static readonly Lazy<string> Base = new(() => Load("base"));
    private static readonly Lazy<string> Cyrillic = new(() => Load("cyrillic"));
    private static readonly Lazy<string> Greek = new(() => Load("greek"));
    private static readonly Lazy<string> Japanese = new(() => Load("japanese"));

    public static IReadOnlyList<char> Build(ExtraScripts scripts)
    {
        IEnumerable<char> chars = Base.Value;
        if (scripts.HasFlag(ExtraScripts.Cyrillic))
        {
            chars = chars.Concat(Cyrillic.Value);
        }

        if (scripts.HasFlag(ExtraScripts.Greek))
        {
            chars = chars.Concat(Greek.Value);
        }

        if (scripts.HasFlag(ExtraScripts.Japanese))
        {
            chars = chars.Concat(Japanese.Value);
        }

        return chars.Distinct().ToList();
    }

    private static string Load(string name)
    {
        using Stream stream = typeof(FontCharsets).Assembly.GetManifestResourceStream($"MiniThemeStudio.Fonts.Charsets.{name}.txt")
            ?? throw new InvalidOperationException($"Missing charset resource '{name}'.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().TrimEnd('\n', '\r');
    }
}
