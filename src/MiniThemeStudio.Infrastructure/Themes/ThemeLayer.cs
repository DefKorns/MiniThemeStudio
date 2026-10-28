namespace MiniThemeStudio.Infrastructure.Themes;

// A theme draft layered over its Base: a file is read from the theme when the theme has it, otherwise from the Base,
// and is always written to the theme. Resource paths are relative to the resources folder, the way
// dependencies.json writes them ("/prefab/bg.scn"; the leading slash is optional).
internal sealed class ThemeLayer
{
    public ThemeLayer(string themePath, string? baseUiPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themePath);
        ThemePath = themePath;
        BaseUiPath = baseUiPath;
    }

    public string ThemePath { get; }

    public string? BaseUiPath { get; }

    // the theme's dependencies.json once the theme has changed it, otherwise the Base's
    public string? DependenciesPath => new[] { ThemePath, BaseUiPath }
        .Where(root => root != null)
        .Select(root => Path.Combine(root!, "dependencies.json"))
        .FirstOrDefault(File.Exists);

    public string ThemeDependenciesPath => Path.Combine(ThemePath, "dependencies.json");

    public string? Resolve(string resourcePath) => ResolveInTheme(resourcePath) ?? ResolveInBase(resourcePath);

    public string? ResolveInTheme(string resourcePath) => Existing(ThemeResourceFile(resourcePath));

    public string? ResolveInBase(string resourcePath) =>
        BaseUiPath is null ? null : Existing(ResourceFile(BaseUiPath, resourcePath));

    // where the theme keeps (or will keep) the resource
    public string ThemeResourceFile(string resourcePath) => ResourceFile(ThemePath, resourcePath);

    public void WriteThemeResource(string resourcePath, string content)
    {
        var path = ThemeResourceFile(resourcePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        EngineJson.WriteAtomically(path, content);
    }

    // resource paths under a folder of resources (e.g. "scripts") found in the theme or the Base, each once
    public IReadOnlyList<string> EnumerateResources(string folder, string searchPattern)
    {
        return new[] { BaseUiPath, ThemePath }
            .Where(root => root != null)
            .Select(root => Path.Combine(root!, "resources"))
            .SelectMany(resources => Directory.Exists(Path.Combine(resources, folder))
                ? Directory.EnumerateFiles(Path.Combine(resources, folder), searchPattern, SearchOption.AllDirectories)
                    .Select(file => "/" + Path.GetRelativePath(resources, file).Replace('\\', '/'))
                : [])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // root + relativePath, refusing paths that would leave root ("..", absolute paths)
    public static string InsideRoot(string root, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(relativePath);

        var rootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var path = Path.GetFullPath(Path.Combine(rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(rootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"The path must stay inside {rootPath}: {relativePath}", nameof(relativePath));
        }

        return path;
    }

    private static string ResourceFile(string root, string resourcePath) =>
        InsideRoot(root, "resources/" + resourcePath.TrimStart('/'));

    private static string? Existing(string path) => File.Exists(path) ? path : null;
}
