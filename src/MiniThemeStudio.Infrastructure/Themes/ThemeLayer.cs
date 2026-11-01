namespace MiniThemeStudio.Infrastructure.Themes;

// a file of the layered theme: FilePath is the theme's copy when it has one, otherwise the Base's
internal sealed record LayeredFile(string ResourcePath, string FilePath, bool IsFromBase, bool OverridesBase);

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
        string path = ThemeResourceFile(resourcePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        EngineJson.WriteAtomically(path, content);
    }

    // resource paths under a folder of resources (e.g. "scripts") found in the theme or the Base, each once
    public IReadOnlyList<string> EnumerateResources(string folder, string searchPattern, bool recursive = true)
    {
        return ListFiles(folder, searchPattern, recursive).Select(file => file.ResourcePath).ToList();
    }

    // the files under a folder of resources found in the theme or the Base, each once, the theme's copy first
    public IReadOnlyList<LayeredFile> ListFiles(string folder, string searchPattern, bool recursive = true)
    {
        Dictionary<string, string> inBase = FindIn(BaseUiPath, folder, searchPattern, recursive);
        Dictionary<string, string> inTheme = FindIn(ThemePath, folder, searchPattern, recursive);
        return inBase.Keys.Union(inTheme.Keys, StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(resourcePath => inTheme.TryGetValue(resourcePath, out string? themeFile)
                ? new LayeredFile(resourcePath, themeFile, false, inBase.ContainsKey(resourcePath))
                : new LayeredFile(resourcePath, inBase[resourcePath], true, false))
            .ToList();
    }

    // the files under a folder of resources in one layer only
    public IReadOnlyList<string> FindFiles(string folder, string searchPattern, bool inBase)
    {
        return FindIn(inBase ? BaseUiPath : ThemePath, folder, searchPattern, false).Values.ToList();
    }

    // the first folder inside a folder of resources (e.g. the console's folder in "sounds"), the theme's first
    public string? FirstSubfolder(string folder)
    {
        return new[] { ThemePath, BaseUiPath }
            .Where(root => root != null)
            .Select(root => Path.Combine(root!, "resources", folder))
            .Where(Directory.Exists)
            .Select(directory => Directory.GetDirectories(directory).Order(StringComparer.OrdinalIgnoreCase).FirstOrDefault())
            .Where(subfolder => subfolder != null)
            .Select(Path.GetFileName)
            .FirstOrDefault();
    }

    // root + relativePath, refusing paths that would leave root ("..", absolute paths)
    public static string InsideRoot(string root, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(relativePath);

        string rootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string path = Path.GetFullPath(Path.Combine(rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(rootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"The path must stay inside {rootPath}: {relativePath}", nameof(relativePath));
        }

        return path;
    }

    // resource path ("/fonts/l.fnt") -> file, for the files of one layer
    private static Dictionary<string, string> FindIn(string? root, string folder, string searchPattern, bool recursive)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (root is null)
        {
            return files;
        }

        string resources = Path.Combine(root, "resources");
        string directory = Path.Combine(resources, folder.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(directory))
        {
            return files;
        }

        foreach (string file in Directory.EnumerateFiles(directory, searchPattern, recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly))
        {
            files["/" + Path.GetRelativePath(resources, file).Replace('\\', '/')] = file;
        }

        return files;
    }

    private static string ResourceFile(string root, string resourcePath) =>
        InsideRoot(root, "resources/" + resourcePath.TrimStart('/'));

    private static string? Existing(string path) => File.Exists(path) ? path : null;
}
