using System.Text.Json.Nodes;
using MiniThemeStudio.Infrastructure.Themes;

namespace MiniThemeStudio.Infrastructure.Tests.Themes;

public sealed class DependencyManifestTests : IDisposable
{
    private const string WallId = "1c75bca3-310d-48f1-95ad-20e7ebc294c6";

    private readonly string _themePath = Directory.CreateTempSubdirectory().FullName;
    private readonly string _basePath = Directory.CreateTempSubdirectory().FullName;

    public DependencyManifestTests()
    {
        File.WriteAllText(Path.Combine(_basePath, "dependencies.json"), $$$"""
            {"resources":[
              {"id":"scene","type":"Scene","dependencies":[{"id":"{{{WallId}}}","refCount":2},{"id":"anim","refCount":1}],"path":"/prefab/bg.scn","groupId":"7","custom":true},
              {"id":"anim","type":"SceneAnimation","path":"/layout/lyt_wallpaper.sceneanimation","groupId":"8"},
              {"id":"{{{WallId}}}","type":"TextureResource","path":"/layout/asset/sprite/wallpaper/wall.png","groupId":"9"},
              {"id":"sprite.a","type":"SpriteResource","content":"{\"textureId\":\"sprite\"}","groupId":"10"}
            ],"resourceGroups":[{"id":"1"}]}
            """);
    }

    public void Dispose()
    {
        Directory.Delete(_themePath, true);
        Directory.Delete(_basePath, true);
    }

    [Fact]
    public void ReadsTheResourcesAndTheirReferences()
    {
        var manifest = DependencyManifest.Load(Layer());

        ManifestResource scene = manifest.FindByPath(DependencyManifest.Scene, "/prefab/bg.scn")!;
        Assert.Equal("scene", scene.Id);
        Assert.Equal([new ManifestReference(WallId, 2), new ManifestReference("anim", 1)], scene.References);
        Assert.True(manifest.IsTexture(WallId));
        Assert.False(manifest.IsTexture("anim"));
        Assert.Equal("""{"textureId":"sprite"}""", manifest.FindById("sprite.a")!.Content);
        Assert.Single(manifest.OfType(DependencyManifest.SceneAnimation));
    }

    [Fact]
    public void ReportsMissingOrBrokenFiles()
    {
        Assert.Null(DependencyManifest.TryLoad(new ThemeLayer(_themePath, null)));
        Assert.Throws<FileNotFoundException>(() => DependencyManifest.Load(new ThemeLayer(_themePath, null)));

        File.WriteAllText(Path.Combine(_themePath, "dependencies.json"), "{\"resources\":");
        Assert.Throws<InvalidDataException>(() => DependencyManifest.Load(Layer()));

        File.WriteAllText(Path.Combine(_themePath, "dependencies.json"), "{}");
        Assert.Throws<InvalidDataException>(() => DependencyManifest.Load(Layer()));
    }

    [Fact]
    public void AddsATextureWithItsSpriteSheet()
    {
        var manifest = DependencyManifest.Load(Layer());

        string textureId = manifest.AddTexture("/layout/asset/sprite/wallpaper/wall_mid.png", "/layout/asset/sprite/wallpaper/wall_mid.json");

        Assert.True(Guid.TryParse(textureId, out _));
        Assert.True(manifest.IsTexture(textureId));
        ManifestResource sheet = manifest.FindById($"sheet_{textureId}")!;
        Assert.Equal(DependencyManifest.SpriteSheet, sheet.Type);
        Assert.Equal([new ManifestReference(textureId, 1)], sheet.References);
        Assert.Throws<IOException>(() => manifest.AddTexture("/layout/asset/sprite/wallpaper/wall.png", "/x.json"));

        manifest.Save(Layer());
        var groups = JsonNode.Parse(File.ReadAllText(Path.Combine(_themePath, "dependencies.json")))!["resources"]!.AsArray()
            .Select(resource => resource!["groupId"]!.GetValue<string>())
            .ToList();
        Assert.Equal(["7", "8", "9", "10", "11", "12"], groups);
    }

    [Fact]
    public void CountsReferencesUpAndDown()
    {
        var manifest = DependencyManifest.Load(Layer());

        manifest.ChangeReferenceCount(DependencyManifest.Scene, "/prefab/bg.scn", WallId, -1);
        manifest.ChangeReferenceCount(DependencyManifest.Scene, "/prefab/bg.scn", "new", 1);
        manifest.ChangeReferenceCount(DependencyManifest.Scene, "/prefab/bg.scn", "anim", -1);
        manifest.ChangeReferenceCount(DependencyManifest.Scene, "/prefab/bg.scn", "absent", -1);

        Assert.Equal(
            [new ManifestReference(WallId, 1), new ManifestReference("new", 1)],
            manifest.FindById("scene")!.References);
        Assert.Throws<InvalidDataException>(() => manifest.ChangeReferenceCount(DependencyManifest.Scene, "/prefab/none.scn", WallId, 1));
    }

    [Fact]
    public void ReplacesOnlyTheMatchingReferences()
    {
        var manifest = DependencyManifest.Load(Layer());

        manifest.ReplaceReferences(
            DependencyManifest.Scene,
            "/prefab/bg.scn",
            id => id == WallId,
            new Dictionary<string, int> { ["frame1"] = 2, ["frame2"] = 1 });

        Assert.Equal(
            [new ManifestReference("anim", 1), new ManifestReference("frame1", 2), new ManifestReference("frame2", 1)],
            manifest.FindById("scene")!.References);
    }

    [Fact]
    public void SavesToTheThemeAndKeepsUnknownFields()
    {
        var manifest = DependencyManifest.Load(Layer());
        manifest.ChangeReferenceCount(DependencyManifest.Scene, "/prefab/bg.scn", WallId, 1);

        manifest.Save(Layer());

        JsonNode saved = JsonNode.Parse(File.ReadAllText(Path.Combine(_themePath, "dependencies.json")))!;
        Assert.Equal("""[{"id":"1"}]""", saved["resourceGroups"]!.ToJsonString());
        Assert.True(saved["resources"]![0]!["custom"]!.GetValue<bool>());
        Assert.DoesNotContain("\"refCount\":3", File.ReadAllText(Path.Combine(_basePath, "dependencies.json")));
        Assert.Equal(3, DependencyManifest.Load(Layer()).FindById("scene")!.References[0].Count);
    }

    private ThemeLayer Layer() => new(_themePath, _basePath);
}
