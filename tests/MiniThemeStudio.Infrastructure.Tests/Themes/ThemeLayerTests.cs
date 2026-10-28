using MiniThemeStudio.Infrastructure.Themes;

namespace MiniThemeStudio.Infrastructure.Tests.Themes;

public sealed class ThemeLayerTests : IDisposable
{
    private readonly string _themePath = Directory.CreateTempSubdirectory().FullName;
    private readonly string _basePath = Directory.CreateTempSubdirectory().FullName;

    public void Dispose()
    {
        Directory.Delete(_themePath, true);
        Directory.Delete(_basePath, true);
    }

    [Fact]
    public void ReadsTheThemeFileFirstThenTheBase()
    {
        WriteFile(_basePath, "resources/prefab/bg.scn", "base");
        WriteFile(_basePath, "resources/prefab/demo.scn", "base");
        WriteFile(_themePath, "resources/prefab/bg.scn", "theme");
        var layer = new ThemeLayer(_themePath, _basePath);

        Assert.Equal("theme", File.ReadAllText(layer.Resolve("/prefab/bg.scn")!));
        Assert.Equal("base", File.ReadAllText(layer.Resolve("prefab/demo.scn")!));
        Assert.Null(layer.ResolveInTheme("/prefab/demo.scn"));
        Assert.Null(layer.Resolve("/prefab/missing.scn"));
        Assert.Null(new ThemeLayer(_themePath, null).Resolve("/prefab/demo.scn"));
    }

    [Fact]
    public void WritesAlwaysGoToTheTheme()
    {
        WriteFile(_basePath, "resources/prefab/bg.scn", "base");
        var layer = new ThemeLayer(_themePath, _basePath);

        layer.WriteThemeResource("/prefab/bg.scn", "edited");

        Assert.Equal("edited", File.ReadAllText(Path.Combine(_themePath, "resources", "prefab", "bg.scn")));
        Assert.Equal("base", File.ReadAllText(Path.Combine(_basePath, "resources", "prefab", "bg.scn")));
        Assert.Equal(Path.Combine(_themePath, "resources", "prefab", "bg.scn"), layer.ThemeResourceFile("/prefab/bg.scn"));
    }

    [Fact]
    public void UsesTheThemeDependenciesOnceTheThemeHasThem()
    {
        var layer = new ThemeLayer(_themePath, _basePath);
        Assert.Null(layer.DependenciesPath);

        WriteFile(_basePath, "dependencies.json", "{}");
        Assert.Equal(Path.Combine(_basePath, "dependencies.json"), layer.DependenciesPath);

        WriteFile(_themePath, "dependencies.json", "{}");
        Assert.Equal(Path.Combine(_themePath, "dependencies.json"), layer.DependenciesPath);
    }

    [Fact]
    public void ListsTheResourcesOfBothLayersOnce()
    {
        WriteFile(_basePath, "resources/scripts/env/colors_usa.lua", "");
        WriteFile(_basePath, "resources/scripts/sys_ui.lua", "");
        WriteFile(_themePath, "resources/scripts/env/colors_usa.lua", "");
        WriteFile(_themePath, "resources/scripts/env/colors_jpn.lua", "");

        var resources = new ThemeLayer(_themePath, _basePath).EnumerateResources("scripts", "colors_*.lua");

        Assert.Equal(["/scripts/env/colors_jpn.lua", "/scripts/env/colors_usa.lua"], resources);
    }

    [Fact]
    public void ListsEachFileOnceAndTellsWhereItComesFrom()
    {
        WriteFile(_basePath, "resources/fonts/l.fnt", "base");
        WriteFile(_basePath, "resources/fonts/s.fnt", "base");
        WriteFile(_themePath, "resources/fonts/l.fnt", "theme");
        WriteFile(_themePath, "resources/fonts/m.fnt", "theme");
        WriteFile(_themePath, "resources/fonts/extra/x.fnt", "theme");

        var files = new ThemeLayer(_themePath, _basePath).ListFiles("fonts", "*.fnt", recursive: false);

        Assert.Equal(["/fonts/l.fnt", "/fonts/m.fnt", "/fonts/s.fnt"], files.Select(file => file.ResourcePath));
        var replaced = files[0];
        Assert.Equal("theme", File.ReadAllText(replaced.FilePath));
        Assert.False(replaced.IsFromBase);
        Assert.True(replaced.OverridesBase);
        Assert.False(files[1].IsFromBase);
        Assert.False(files[1].OverridesBase);
        Assert.True(files[2].IsFromBase);
    }

    [Fact]
    public void FindsTheFilesOfOneLayer()
    {
        WriteFile(_basePath, "resources/sounds/hvc/bgm_home.wav", "base");
        WriteFile(_themePath, "resources/sounds/hvc/bgm_home.mp3", "theme");
        var layer = new ThemeLayer(_themePath, _basePath);

        Assert.Equal([Path.Combine(_themePath, "resources", "sounds", "hvc", "bgm_home.mp3")], layer.FindFiles("sounds/hvc", "bgm_home.*", inBase: false));
        Assert.Equal([Path.Combine(_basePath, "resources", "sounds", "hvc", "bgm_home.wav")], layer.FindFiles("sounds/hvc", "bgm_home.*", inBase: true));
        Assert.Empty(new ThemeLayer(_themePath, null).FindFiles("sounds/hvc", "*.*", inBase: true));
    }

    [Fact]
    public void FindsTheFirstSubfolderInTheThemeThenInTheBase()
    {
        WriteFile(_basePath, "resources/sounds/sfc/bgm_home.wav", "");
        var layer = new ThemeLayer(_themePath, _basePath);
        Assert.Equal("sfc", layer.FirstSubfolder("sounds"));

        WriteFile(_themePath, "resources/sounds/hvc/bgm_home.wav", "");
        Assert.Equal("hvc", layer.FirstSubfolder("sounds"));
        Assert.Null(layer.FirstSubfolder("missing"));
    }

    [Theory]
    [InlineData("../outside.png")]
    [InlineData("resources/../../outside.png")]
    public void RefusesPathsThatLeaveTheRoot(string relativePath)
    {
        Assert.Throws<ArgumentException>(() => ThemeLayer.InsideRoot(_themePath, relativePath));
        Assert.Throws<ArgumentException>(() => new ThemeLayer(_themePath, _basePath).ThemeResourceFile("/../../outside.png"));
    }

    private static void WriteFile(string root, string relativePath, string content)
    {
        var path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
