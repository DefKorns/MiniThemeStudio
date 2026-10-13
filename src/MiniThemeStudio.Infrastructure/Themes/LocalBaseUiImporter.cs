using System.Text.Json;
using MiniThemeStudio.Core.Themes;

namespace MiniThemeStudio.Infrastructure.Themes;

public sealed class LocalBaseUiImporter(IBaseUiValidator validator) : ILocalBaseUiImporter
{
    public string Import(string sourcePath, string baseUiRootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUiRootPath);
        validator.Validate(sourcePath);

        var sourceDirectory = new DirectoryInfo(sourcePath);
        var destinationName = DetectConsoleType(sourceDirectory.FullName);
        var destinationPath = Path.Combine(baseUiRootPath, destinationName);

        if (PathsEqual(sourceDirectory.FullName, destinationPath))
        {
            return destinationPath;
        }

        Directory.CreateDirectory(baseUiRootPath);
        var stagingPath = Path.Combine(
            baseUiRootPath,
            $".{destinationName}.mts-stage-{Guid.NewGuid():N}");
        var backupPath = Path.Combine(
            baseUiRootPath,
            $".{destinationName}.mts-backup-{Guid.NewGuid():N}");
        var destinationMoved = false;

        try
        {
            CopyDirectory(sourceDirectory, Directory.CreateDirectory(stagingPath));
            validator.Validate(stagingPath);

            if (Directory.Exists(destinationPath))
            {
                Directory.Move(destinationPath, backupPath);
                destinationMoved = true;
            }

            Directory.Move(stagingPath, destinationPath);
            DeleteDirectory(backupPath);
            return destinationPath;
        }
        catch
        {
            DeleteDirectory(stagingPath);
            if (destinationMoved && !Directory.Exists(destinationPath))
            {
                Directory.Move(backupPath, destinationPath);
            }

            throw;
        }
    }

    private static string DetectConsoleType(string sourcePath)
    {
        var initPath = Path.Combine(sourcePath, "init.json");
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(initPath));
            var root = document.RootElement;
            var engineFormat = root.TryGetProperty("engineFormat", out var formatElement)
                ? formatElement.GetInt32()
                : 0;
            var scripts = root.TryGetProperty("luaScripts", out var scriptsElement)
                ? scriptsElement.EnumerateArray()
                    .Select(script => script.GetString() ?? "")
                    .ToArray()
                : [];

            if (engineFormat >= 10)
            {
                return "shonen";
            }

            if (scripts.Any(script => script.EndsWith("/env_eur.lua", StringComparison.Ordinal)))
            {
                return "snes-eur";
            }

            if (scripts.Any(script => script.EndsWith("/env_usa.lua", StringComparison.Ordinal)))
            {
                return "snes-usa";
            }

            if (scripts.Any(script => script.EndsWith("/env_jpn.lua", StringComparison.Ordinal)))
            {
                return "snes-jpn";
            }

            if (scripts.Any(script => script.EndsWith("/colors_nes.lua", StringComparison.Ordinal)))
            {
                return "nes";
            }

            if (scripts.Any(script => script.EndsWith("/colors_hvc.lua", StringComparison.Ordinal)))
            {
                return "famicom";
            }
        }
        catch (Exception exception) when (
            exception is IOException or JsonException or InvalidOperationException)
        {
            throw new InvalidDataException("Could not read the Base UI console type.", exception);
        }

        throw new InvalidDataException("Could not detect the Base UI console type.");
    }

    private static bool PathsEqual(string firstPath, string secondPath)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(firstPath)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(secondPath)),
            comparison);
    }

    private static void CopyDirectory(DirectoryInfo source, DirectoryInfo destination)
    {
        foreach (var directory in source.EnumerateDirectories("*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(source.FullName, directory.FullName);
            Directory.CreateDirectory(Path.Combine(destination.FullName, relativePath));
        }

        foreach (var file in source.EnumerateFiles("*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(source.FullName, file.FullName);
            var destinationPath = Path.Combine(destination.FullName, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            file.CopyTo(destinationPath);
        }
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }
    }
}
