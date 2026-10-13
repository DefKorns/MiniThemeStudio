namespace MiniThemeStudio.Core.Themes;

public interface IThemeExporter
{
    int ExportZip(string themePath, string zipPath, string rootFolderName);
}
