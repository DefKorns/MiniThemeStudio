using System.Text;
using MiniThemeStudio.Core.Themes;

namespace MiniThemeStudio.Infrastructure.Themes;

public sealed class ThemeProjectCreator : IThemeProjectCreator
{
    private readonly IBaseUiValidator _baseUiValidator;

    public ThemeProjectCreator() : this(new BaseUiValidator())
    {
    }

    public ThemeProjectCreator(IBaseUiValidator baseUiValidator)
    {
        _baseUiValidator = baseUiValidator;
    }

    public void Create(NewThemeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BaseThemePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DestinationPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Author);
        _baseUiValidator.Validate(request.BaseThemePath);

        var source = new DirectoryInfo(request.BaseThemePath);

        var destination = new DirectoryInfo(request.DestinationPath);
        if (!destination.Exists)
        {
            throw new DirectoryNotFoundException(
                $"Destination directory not found: {destination.FullName}");
        }

        if (destination.EnumerateFileSystemInfos().Any())
        {
            throw new IOException("The destination directory must be empty.");
        }

        CopyDirectory(source, destination);
        var encoding = new UTF8Encoding(false);
        File.WriteAllText(
            Path.Combine(destination.FullName, "title.txt"),
            request.Name.Trim(),
            encoding);
        File.WriteAllText(
            Path.Combine(destination.FullName, "author.txt"),
            request.Author.Trim(),
            encoding);
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
}
