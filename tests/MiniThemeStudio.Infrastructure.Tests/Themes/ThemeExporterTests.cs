using System.Formats.Tar;
using System.IO.Compression;
using MiniThemeStudio.Infrastructure.Themes;

namespace MiniThemeStudio.Infrastructure.Tests.Themes;

public sealed class ThemeExporterTests : IDisposable
{
    private readonly string _rootPath = Directory.CreateTempSubdirectory().FullName;
    private readonly string _themePath;

    public ThemeExporterTests()
    {
        _themePath = Path.Combine(_rootPath, "draft");
        WriteFile(_themePath, "resources/sprites/packed.png", "png");
        WriteFile(_themePath, "dependencies.json", "{}");
        WriteFile(_themePath, "title.txt", "My Theme");
        WriteFile(_themePath, "author.txt", "Me");
        WriteFile(_themePath, "info", "NAME=My Theme");
        WriteFile(_themePath, ".mts-base.json", "{}");
    }

    public void Dispose()
    {
        Directory.Delete(_rootPath, true);
    }

    [Fact]
    public void ExportsPackageInOmThemesLayout()
    {
        var previewPath = Path.Combine(_rootPath, "my_theme.png");
        File.WriteAllText(previewPath, "preview");
        var outputPath = Path.Combine(_rootPath, "out");

        var result = new ThemeExporter().ExportPackage(_themePath, outputPath, "snes", "my_theme", previewPath, null);

        Assert.Equal(Path.Combine(outputPath, "SNES.my_theme.tar.gz"), result.PackagePath);
        Assert.True(result.IncludesPreview);
        Assert.Equal(2, result.FileCount);
        Assert.Equal(
            [
                "my_theme.png",
                "my_theme/",
                "my_theme/dependencies.json",
                "my_theme/resources/",
                "my_theme/resources/sprites/",
                "my_theme/resources/sprites/packed.png",
            ],
            ReadEntryNames(result.PackagePath).Order(StringComparer.Ordinal));
        Assert.Empty(Directory.GetFiles(outputPath, "*.tmp"));
    }

    [Fact]
    public void ExportsPackageWithoutPreviewWhenNoneExists()
    {
        var result = new ThemeExporter().ExportPackage(_themePath, _rootPath, "shonen", "my_theme", Path.Combine(_rootPath, "missing.png"), null);

        Assert.False(result.IncludesPreview);
        Assert.EndsWith("SHONEN.my_theme.tar.gz", result.PackagePath, StringComparison.Ordinal);
        Assert.DoesNotContain("my_theme.png", ReadEntryNames(result.PackagePath));
    }

    [Fact]
    public void LeavesOutFilesIdenticalToTheBase()
    {
        var basePath = Path.Combine(_rootPath, "base");
        WriteFile(basePath, "dependencies.json", "{}");
        WriteFile(basePath, "resources/sprites/packed.png", "stock png");
        WriteFile(basePath, "resources/prefab/bg.scn", "scene");
        WriteFile(_themePath, "resources/prefab/bg.scn", "scene");

        var result = new ThemeExporter().ExportPackage(_themePath, Path.Combine(_rootPath, "out"), "snes", "my_theme", null, basePath);

        Assert.Equal(1, result.FileCount);
        Assert.Equal(2, result.UnchangedFilesSkipped);
        Assert.Equal(
            ["my_theme/", "my_theme/resources/", "my_theme/resources/sprites/", "my_theme/resources/sprites/packed.png"],
            ReadEntryNames(result.PackagePath).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void MarksPackagesWithNewFilesForOverlay()
    {
        var basePath = Path.Combine(_rootPath, "base");
        WriteFile(basePath, "resources/sprites/packed.png", "stock png");
        WriteFile(_themePath, "resources/layout/asset/sprite/mario/mario_run04.png", "new frame");

        var withNewFile = new ThemeExporter().ExportPackage(_themePath, Path.Combine(_rootPath, "a"), "snes", "my_theme", null, basePath);
        File.Delete(Path.Combine(_themePath, "resources", "layout", "asset", "sprite", "mario", "mario_run04.png"));
        var changedOnly = new ThemeExporter().ExportPackage(_themePath, Path.Combine(_rootPath, "b"), "snes", "my_theme", null, basePath);

        Assert.Contains("my_theme/needs_overlay", ReadEntryNames(withNewFile.PackagePath));
        Assert.DoesNotContain("my_theme/needs_overlay", ReadEntryNames(changedOnly.PackagePath));
    }

    [Fact]
    public void RejectsInvalidNames()
    {
        Assert.Throws<ArgumentException>(() => new ThemeExporter().ExportPackage(_themePath, _rootPath, "snes", "../escape", null, null));
    }

    private static List<string> ReadEntryNames(string packagePath)
    {
        using var stream = File.OpenRead(packagePath);
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var reader = new TarReader(gzip);
        var names = new List<string>();
        while (reader.GetNextEntry() is { } entry)
        {
            names.Add(entry.Name);
        }

        return names;
    }

    private static void WriteFile(string root, string relativePath, string content)
    {
        var path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
