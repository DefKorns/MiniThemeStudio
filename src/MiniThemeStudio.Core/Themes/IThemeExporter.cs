namespace MiniThemeStudio.Core.Themes;

public sealed record ThemePackageResult(string PackagePath, int FileCount, bool IncludesPreview);

public interface IThemeExporter
{
    ThemePackageResult ExportPackage(string themePath, string outputDirectory, string system, string name, string? previewPath);

    string SendToPackaging(string themePath, string omThemesRootPath, string system, string name);
}
