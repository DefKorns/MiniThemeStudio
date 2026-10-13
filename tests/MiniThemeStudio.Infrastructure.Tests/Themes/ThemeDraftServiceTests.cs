using MiniThemeStudio.Core.Themes;
using MiniThemeStudio.Infrastructure.Themes;

namespace MiniThemeStudio.Infrastructure.Tests.Themes;

public sealed class ThemeDraftServiceTests
{
    [Fact]
    public void OpenExistingDoesNotChangeSourceUntilSave()
    {
        var rootPath = Directory.CreateTempSubdirectory().FullName;
        var themePath = Path.Combine(rootPath, "theme");
        Directory.CreateDirectory(themePath);
        var sourceFilePath = Path.Combine(themePath, "asset.txt");
        File.WriteAllText(sourceFilePath, "original");
        var service = CreateService();
        var draft = service.OpenExisting(themePath);

        try
        {
            File.WriteAllText(Path.Combine(draft.DraftPath, "asset.txt"), "edited");

            Assert.Equal("original", File.ReadAllText(sourceFilePath));

            service.Save(draft);

            Assert.Equal("edited", File.ReadAllText(sourceFilePath));
        }
        finally
        {
            service.Discard(draft);
            Directory.Delete(rootPath, true);
        }
    }

    [Fact]
    public void CreateNewLeavesDestinationEmptyUntilSave()
    {
        var rootPath = Directory.CreateTempSubdirectory().FullName;
        var basePath = Path.Combine(rootPath, "base");
        var destinationPath = Path.Combine(rootPath, "new-theme");
        Directory.CreateDirectory(basePath);
        Directory.CreateDirectory(Path.Combine(basePath, "resources"));
        File.WriteAllText(Path.Combine(basePath, "dependencies.json"), "{}");
        File.WriteAllText(Path.Combine(basePath, "init.json"), "{}");
        File.WriteAllText(Path.Combine(basePath, "asset.txt"), "base");
        var service = CreateService();
        var draft = service.CreateNew(new NewThemeRequest(
            basePath,
            destinationPath,
            "Draft Theme",
            "Draft Author"));

        try
        {
            Assert.False(Directory.Exists(destinationPath));
            Assert.Equal(
                "Draft Theme",
                File.ReadAllText(Path.Combine(draft.DraftPath, "title.txt")));

            draft = service.Save(draft);

            Assert.Equal(
                "Draft Theme",
                File.ReadAllText(Path.Combine(destinationPath, "title.txt")));
            Assert.Equal("base", File.ReadAllText(Path.Combine(destinationPath, "asset.txt")));

            File.WriteAllText(Path.Combine(draft.DraftPath, "asset.txt"), "edited");
            draft = service.Save(draft);

            Assert.Equal("edited", File.ReadAllText(Path.Combine(destinationPath, "asset.txt")));
        }
        finally
        {
            service.Discard(draft);
            Directory.Delete(rootPath, true);
        }
    }

    [Fact]
    public void DiscardDeletesDraftWithoutChangingSource()
    {
        var rootPath = Directory.CreateTempSubdirectory().FullName;
        var themePath = Path.Combine(rootPath, "theme");
        Directory.CreateDirectory(themePath);
        var sourceFilePath = Path.Combine(themePath, "asset.txt");
        File.WriteAllText(sourceFilePath, "original");
        var service = CreateService();
        var draft = service.OpenExisting(themePath);
        File.WriteAllText(Path.Combine(draft.DraftPath, "asset.txt"), "edited");

        service.Discard(draft);

        Assert.False(Directory.Exists(draft.DraftPath));
        Assert.Equal("original", File.ReadAllText(sourceFilePath));
        Directory.Delete(rootPath, true);
    }

    [Fact]
    public void CreatesDraftUnderConfiguredRoot()
    {
        var rootPath = Directory.CreateTempSubdirectory().FullName;
        var themePath = Path.Combine(rootPath, "theme");
        var draftsPath = Path.Combine(rootPath, "custom-drafts");
        Directory.CreateDirectory(themePath);
        var service = new ThemeDraftService(
            new ThemeProjectCreator(),
            () => draftsPath);
        var draft = service.OpenExisting(themePath);

        try
        {
            Assert.Equal(
                Path.GetFullPath(draftsPath),
                Directory.GetParent(draft.DraftPath)!.FullName);
        }
        finally
        {
            service.Discard(draft);
            Directory.Delete(rootPath, true);
        }
    }

    private static ThemeDraftService CreateService()
    {
        return new ThemeDraftService(new ThemeProjectCreator());
    }
}
