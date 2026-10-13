namespace MiniThemeStudio.Core.Themes;

public interface IThemeResourceIndexer
{
    ThemeResourceReport Analyze(string themePath, string? baseUiPath = null);
}
