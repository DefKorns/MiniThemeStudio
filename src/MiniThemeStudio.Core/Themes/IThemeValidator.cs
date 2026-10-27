namespace MiniThemeStudio.Core.Themes;

public interface IThemeValidator
{
    ThemeValidationResult Validate(string themeDraftPath, string? baseUiPath = null, ThemeValidationOptions? options = null);
}
