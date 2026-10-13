namespace MiniThemeStudio.Core.Themes;

public interface ILocalBaseUiImporter
{
    string Import(string sourcePath, string baseUiRootPath);
}
