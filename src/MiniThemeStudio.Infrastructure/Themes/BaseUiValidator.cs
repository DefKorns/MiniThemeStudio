using MiniThemeStudio.Core.Themes;

namespace MiniThemeStudio.Infrastructure.Themes;

public sealed class BaseUiValidator : IBaseUiValidator
{
    public void Validate(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException($"Base UI directory not found: {path}");
        }

        var missingEntries = new List<string>();

        if (!Directory.Exists(Path.Combine(path, "resources")))
        {
            missingEntries.Add("resources/");
        }

        if (!File.Exists(Path.Combine(path, "dependencies.json")))
        {
            missingEntries.Add("dependencies.json");
        }

        if (!File.Exists(Path.Combine(path, "init.json")))
        {
            missingEntries.Add("init.json");
        }

        if (missingEntries.Count > 0)
        {
            throw new InvalidDataException(
                $"Invalid Base UI. Missing: {string.Join(", ", missingEntries)}");
        }
    }
}
