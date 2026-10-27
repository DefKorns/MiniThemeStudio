using MiniThemeStudio.Infrastructure.Themes;

namespace MiniThemeStudio.Infrastructure.Tests.Themes;

public sealed class ThemeResourceIndexerTests : IDisposable
{
    private const string GokuId = "3e60e2df-39af-42b8-b28c-9ac3cd1a785c";
    private const string WallId = "1c75bca3-310d-48f1-95ad-20e7ebc294c6";
    private const string MarioId = "a9d1ddbf-8974-4a28-bce9-bf3ab3c87786";

    private readonly string _themePath = Directory.CreateTempSubdirectory().FullName;
    private readonly string _basePath = Directory.CreateTempSubdirectory().FullName;

    public ThemeResourceIndexerTests()
    {
        WriteFile(_basePath, "dependencies.json", $$$"""
            {"resources":[
              {"id":"scene","type":"Scene","path":"/prefab/bg.scn","groupId":"1"},
              {"id":"anim","type":"SceneAnimation","path":"/layout/lyt_mario.sceneanimation","groupId":"2"},
              {"id":"{{{WallId}}}","type":"TextureResource","path":"/layout/asset/sprite/wallpaper/wall.png","groupId":"3"},
              {"id":"sheet_{{{WallId}}}","type":"SpriteSheet","dependencies":[{"id":"{{{WallId}}}","refCount":1}],"path":"/layout/asset/sprite/wallpaper/wall.json","groupId":"4"},
              {"id":"{{{GokuId}}}","type":"TextureResource","path":"/sprites/sampleasset/goku.png","groupId":"5"},
              {"id":"sheet_{{{GokuId}}}","type":"SpriteSheet","dependencies":[{"id":"{{{GokuId}}}","refCount":1}],"path":"/sprites/sampleasset/goku.json","groupId":"6"},
              {"id":"{{{MarioId}}}","type":"TextureResource","path":"/layout/asset/sprite/mario/mario_run01.png","groupId":"7"}
            ],"resourceGroups":[]}
            """);
        WriteFile(_basePath, "resources/prefab/bg.scn", $$$"""
            {"rootWorldNode":{"name":"BG","components":[],"children":[
              {"name":"Wall","components":[{"type":"TextureComponent","textureId":"{{{WallId}}}","size":[1536,552]}],"children":[]},
              {"name":"Goku","components":[{"type":"TextureComponent","textureId":"{{{GokuId}}}","size":[48,48]}],"children":[]}
            ]}}
            """);
        WriteFile(_basePath, "resources/layout/lyt_mario.sceneanimation", $$$"""
            {"duration":1,"animations":[{"componentType":"TextureComponent","propertyId":"Texture","keyCurves":[{"time":0,"value":"{{{MarioId}}}"}]}]}
            """);
        WriteFile(_basePath, "resources/layout/asset/sprite/wallpaper/wall.png", "png");
        WriteFile(_basePath, "resources/layout/asset/sprite/wallpaper/wall.json", $$$"""{"texture":{"id":"{{{WallId}}}","width":512,"height":184},"sprites":{}}""");
        WriteFile(_basePath, "resources/layout/asset/sprite/mario/mario_run01.png", "png");
    }

    public void Dispose()
    {
        Directory.Delete(_themePath, true);
        Directory.Delete(_basePath, true);
    }

    [Fact]
    public void MapsSceneUsageAndReportsMissingTextureImages()
    {
        var report = new ThemeResourceIndexer().Analyze(_themePath, _basePath);

        Assert.Equal(["bg.scn"], report.UsageOf(WallId));
        Assert.Equal(["lyt_mario.sceneanimation"], report.UsageOf(MarioId));
        var missing = Assert.Single(report.MissingTextures);
        Assert.Equal(GokuId, missing.TextureId);
        Assert.Equal(Path.Combine("resources", "sprites", "sampleasset", "goku.png"), missing.ImageRelativePath);
        Assert.Equal(Path.Combine("resources", "sprites", "sampleasset", "goku.json"), missing.DefinitionRelativePath);
        Assert.Contains(report.Issues, issue => issue.IsError && issue.Message.Contains("goku.png"));
    }

    [Fact]
    public void ThemeFilesResolveTexturesAndOverrideBaseScenes()
    {
        WriteFile(_themePath, "resources/sprites/sampleasset/goku.png", "png");
        WriteFile(_themePath, "resources/prefab/bg.scn", """{"rootWorldNode":{"name":"BG","components":[],"children":[]}}""");

        var report = new ThemeResourceIndexer().Analyze(_themePath, _basePath);

        Assert.Empty(report.MissingTextures);
        Assert.Empty(report.UsageOf(WallId));
        Assert.DoesNotContain(report.Issues, issue => issue.IsError);
    }

    [Fact]
    public void ResolvesTextureIdsByRegisteredImagePath()
    {
        var report = new ThemeResourceIndexer().Analyze(_themePath, _basePath);

        Assert.Equal(GokuId, report.TextureIdOf("sprites/sampleasset/goku.png", "id-from-image-json"));
        Assert.Equal(WallId, report.TextureIdOf(@"layout\asset\sprite\wallpaper\WALL.png"));
        Assert.Equal("id-from-image-json", report.TextureIdOf("sprites/unregistered.png", "id-from-image-json"));
    }

    [Fact]
    public void ReportsUnregisteredTextureReferences()
    {
        WriteFile(_themePath, "resources/sprites/sampleasset/goku.png", "png");
        WriteFile(_themePath, "resources/prefab/bg.scn", """
            {"rootWorldNode":{"name":"BG","components":[{"type":"TextureComponent","textureId":"00000000-0000-0000-0000-000000000000"}],"children":[]}}
            """);

        var report = new ThemeResourceIndexer().Analyze(_themePath, _basePath);

        Assert.Contains(report.Issues, issue => issue.IsError && issue.Message.Contains("not registered in dependencies.json"));
    }

    [Fact]
    public void WarnsWhenResizedTextureKeepsOriginalSceneScale()
    {
        WriteFile(_themePath, "resources/sprites/sampleasset/goku.png", "png");
        WriteFile(_themePath, "resources/layout/asset/sprite/wallpaper/wall.json", $$$"""{"texture":{"id":"{{{WallId}}}","width":512,"height":300},"sprites":{}}""");

        var report = new ThemeResourceIndexer().Analyze(_themePath, _basePath);

        Assert.Contains(report.Issues, issue => !issue.IsError && issue.Message.Contains("wall.json is 512x300 instead of the original 512x184"));
    }

    [Fact]
    public void TreatsNonStrictSceneJsonAsWarning()
    {
        WriteFile(_themePath, "resources/sprites/sampleasset/goku.png", "png");
        WriteFile(_themePath, "resources/layout/lyt_mario.sceneanimation", """{"duration":1,"animations":[{"value":[0.,1]}]}""");

        var report = new ThemeResourceIndexer().Analyze(_themePath, _basePath);

        Assert.Contains(report.Issues, issue => !issue.IsError && issue.Message.Contains("not strict JSON"));
        Assert.DoesNotContain(report.Issues, issue => issue.IsError);
    }

    private static void WriteFile(string root, string relativePath, string content)
    {
        var path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
