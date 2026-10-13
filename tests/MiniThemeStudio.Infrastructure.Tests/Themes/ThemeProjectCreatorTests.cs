using MiniThemeStudio.Core.Themes;
using MiniThemeStudio.Infrastructure.Themes;

namespace MiniThemeStudio.Infrastructure.Tests.Themes;

public sealed class ThemeProjectCreatorTests
{
    [Fact]
    public void CreateCopiesBaseAndWritesMetadata()
    {
        var rootPath = Directory.CreateTempSubdirectory().FullName;
        var basePath = Path.Combine(rootPath, "base");
        var destinationPath = Path.Combine(rootPath, "theme");
        Directory.CreateDirectory(Path.Combine(basePath, "resources", "sprites"));
        Directory.CreateDirectory(destinationPath);
        File.WriteAllText(Path.Combine(basePath, "dependencies.json"), "{}");
        File.WriteAllText(Path.Combine(basePath, "init.json"), "{}");
        File.WriteAllText(
            Path.Combine(basePath, "resources", "sprites", "packed.json"),
            "{}");

        try
        {
            new ThemeProjectCreator().Create(new NewThemeRequest(
                basePath,
                destinationPath,
                "My Theme",
                "Theme Author"));

            Assert.True(File.Exists(
                Path.Combine(destinationPath, "resources", "sprites", "packed.json")));
            Assert.Equal("My Theme", File.ReadAllText(Path.Combine(destinationPath, "title.txt")));
            Assert.Equal(
                "Theme Author",
                File.ReadAllText(Path.Combine(destinationPath, "author.txt")));
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    [Fact]
    public void CreateRejectsNonEmptyDestination()
    {
        var rootPath = Directory.CreateTempSubdirectory().FullName;
        var basePath = Path.Combine(rootPath, "base");
        var destinationPath = Path.Combine(rootPath, "theme");
        Directory.CreateDirectory(basePath);
        Directory.CreateDirectory(Path.Combine(basePath, "resources"));
        Directory.CreateDirectory(destinationPath);
        File.WriteAllText(Path.Combine(basePath, "dependencies.json"), "{}");
        File.WriteAllText(Path.Combine(basePath, "init.json"), "{}");
        File.WriteAllText(Path.Combine(destinationPath, "existing.txt"), "existing");

        try
        {
            var action = () => new ThemeProjectCreator().Create(new NewThemeRequest(
                basePath,
                destinationPath,
                "My Theme",
                "Theme Author"));

            var exception = Assert.Throws<IOException>(action);
            Assert.Equal("The destination directory must be empty.", exception.Message);
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }
}
