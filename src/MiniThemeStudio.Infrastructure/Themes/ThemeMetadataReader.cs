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

        var title = ReadOptionalText(Path.Combine(rootPath, "title.txt"));
        var infoPath = Path.Combine(rootPath, "info");
        var infoLines = File.Exists(infoPath) ? File.ReadLines(infoPath) : [];
        var author = ReadOptionalText(Path.Combine(rootPath, "author.txt"));
        var name = ThemeMetadataResolver.ResolveName(fallbackName ?? directory.Name, title, infoLines);

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

        var value = File.ReadAllText(path).Trim();
        return value.Length == 0 ? null : value;
    }
}
