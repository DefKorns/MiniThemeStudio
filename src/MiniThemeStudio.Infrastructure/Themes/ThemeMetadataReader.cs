using MiniThemeStudio.Core.Themes;

namespace MiniThemeStudio.Infrastructure.Themes;

public sealed class ThemeMetadataReader : IThemeMetadataReader
{
    public ThemeMetadata Read(string rootPath, string? fallbackName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        var directory = new DirectoryInfo(rootPath);
        if (!directory.Exists)
        {
            throw new DirectoryNotFoundException($"Theme directory not found: {rootPath}");
        }

        string? title = ReadOptionalText(Path.Combine(rootPath, "title.txt"));
        string infoPath = Path.Combine(rootPath, "info");
        IEnumerable<string> infoLines = File.Exists(infoPath) ? File.ReadLines(infoPath) : [];
        string? author = ReadOptionalText(Path.Combine(rootPath, "author.txt"));
        string name = ThemeMetadataResolver.ResolveName(fallbackName ?? directory.Name, title, infoLines);

        return new ThemeMetadata(
            directory.FullName,
            name,
            author,
            File.Exists(Path.Combine(rootPath, "needs_overlay")));
    }

    private static string? ReadOptionalText(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        string value = File.ReadAllText(path).Trim();
        return value.Length == 0 ? null : value;
    }
}
