namespace MiniThemeStudio.Core.Themes;

public interface IThemeMetadataReader
{
    ThemeMetadata Read(string rootPath, string? fallbackName = null);
}
