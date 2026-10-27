namespace MiniThemeStudio.Core.Themes;

public sealed record ThemePackageResult(string PackagePath, int FileCount, bool IncludesPreview, int UnchangedFilesSkipped);

public interface IThemeExporter
{
    ThemePackageResult ExportPackage(string themePath, string outputDirectory, string system, string name, string? previewPath, string? baseUiPath, ThemeExportOptions? options = null);
}
