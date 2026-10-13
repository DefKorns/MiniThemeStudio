using MiniThemeStudio.Core.Themes;
using MiniThemeStudio.Infrastructure.Themes;

namespace MiniThemeStudio.Infrastructure.Tests.Themes;

public sealed class TextureAtlasReaderTests
{
    [Fact]
    public void ReadReturnsNullWhenPackedDefinitionDoesNotExist()
    {
        var rootPath = Directory.CreateTempSubdirectory().FullName;

        try
        {
            var atlas = new TextureAtlasReader().Read(rootPath);

            Assert.Null(atlas);
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    [Fact]
    public void ReadMapsTextureAndSpriteData()
    {
        var rootPath = Directory.CreateTempSubdirectory().FullName;
        var spritesPath = Path.Combine(rootPath, "resources", "sprites");
        Directory.CreateDirectory(spritesPath);
        File.WriteAllText(
            Path.Combine(spritesPath, "packed.json"),
            """
            {
              "texture": { "id": "texture-id", "width": 512, "height": 1024 },
              "sprites": {
                "arrow_left.png": {
                  "textureFrame": [495, 481, 16, 22],
                  "pivot": [25.5, 11],
                  "sourceSize": [16, 22],
                  "sourceOffset": [0, 0]
                }
              }
            }
            """);

        try
        {
            var atlas = new TextureAtlasReader().Read(rootPath);

            Assert.NotNull(atlas);
            Assert.Equal("texture-id", atlas.TextureId);
            Assert.Equal(512, atlas.Size.Width);
            Assert.Equal(1024, atlas.Size.Height);
            var sprite = Assert.Single(atlas.Sprites);
            Assert.Equal("arrow_left.png", sprite.Name);
            Assert.Equal(495, sprite.Frame.X);
            Assert.Equal(22, sprite.Frame.Height);
            Assert.Equal(25.5, sprite.Pivot.X);
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    [Fact]
    public void ReadAllFindsNesAtlasesFromBaseAndPrefersThemeImages()
    {
        var themeRootPath = Directory.CreateTempSubdirectory().FullName;
        var baseRootPath = Directory.CreateTempSubdirectory().FullName;
        var themeSpritesPath = Path.Combine(themeRootPath, "resources", "sprites");
        var baseSpritesPath = Path.Combine(baseRootPath, "resources", "sprites");
        Directory.CreateDirectory(themeSpritesPath);
        Directory.CreateDirectory(baseSpritesPath);
        File.WriteAllText(Path.Combine(baseSpritesPath, "nes.json"), AtlasJson("nes", 2));
        File.WriteAllText(Path.Combine(baseSpritesPath, "dot_num.json"), AtlasJson("dots", 11));
        File.WriteAllText(Path.Combine(baseSpritesPath, "dummy.json"), AtlasJson("dummy", 1));
        File.WriteAllText(Path.Combine(baseSpritesPath, "broken.json"), """{"sprites":{"a":{},"b":{}}}""");
        var themeImagePath = Path.Combine(themeSpritesPath, "nes.png");
        File.WriteAllBytes(themeImagePath, [1]);

        try
        {
            var atlases = new TextureAtlasReader().ReadAll(themeRootPath, baseRootPath);

            Assert.Equal(["nes", "dot_num"], atlases.Select(atlas => atlas.Name));
            Assert.Equal(themeImagePath, atlases[0].ImagePath);
            Assert.Equal(Path.Combine(baseSpritesPath, "dot_num.png"), atlases[1].ImagePath);
            Assert.Equal(11, atlases[1].Sprites.Count);
            Assert.Equal(Path.Combine("resources", "sprites", "nes.png"), atlases[0].RelativeImagePath);
        }
        finally
        {
            Directory.Delete(themeRootPath, true);
            Directory.Delete(baseRootPath, true);
        }
    }

    [Fact]
    public void ReadDefaultsMissingSpriteMetrics()
    {
        var rootPath = Directory.CreateTempSubdirectory().FullName;
        var spritesPath = Path.Combine(rootPath, "resources", "sprites");
        Directory.CreateDirectory(spritesPath);
        File.WriteAllText(
            Path.Combine(spritesPath, "packed.json"),
            """{"texture":{"id":"t","width":64,"height":64},"sprites":{"icon":{"textureFrame":[4,8,20,10]}}}""");

        try
        {
            var sprite = Assert.Single(new TextureAtlasReader().Read(rootPath)!.Sprites);

            Assert.Equal(new AtlasPoint(10, 5), sprite.Pivot);
            Assert.Equal(new AtlasSize(20, 10), sprite.SourceSize);
            Assert.Equal(new AtlasPoint(0, 0), sprite.SourceOffset);
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    [Fact]
    public void DependencyFramesOverridePackedDefinition()
    {
        var themeRootPath = Directory.CreateTempSubdirectory().FullName;
        var baseRootPath = Directory.CreateTempSubdirectory().FullName;
        var baseSpritesPath = Path.Combine(baseRootPath, "resources", "sprites");
        Directory.CreateDirectory(baseSpritesPath);
        File.WriteAllText(Path.Combine(baseSpritesPath, "packed.json"), AtlasJson("tex", 2));
        File.WriteAllText(Path.Combine(themeRootPath, "dependencies.json"), """
            {"resources":[
              {"id":"tex.s1","type":"SpriteResource","content":"{\"textureId\":\"tex\",\"textureFrame\":[20,10,4,6],\"pivot\":[2,3]}"},
              {"id":"tex.extra.png","type":"SpriteResource","content":"{\"textureId\":\"tex\",\"textureFrame\":[0,20,8,8],\"pivot\":[4,4]}"},
              {"id":"other.s0","type":"SpriteResource","content":"{\"textureId\":\"other\",\"textureFrame\":[9,9,9,9],\"pivot\":[0,0]}"}
            ],"resourceGroups":[]}
            """);

        try
        {
            var sprites = new TextureAtlasReader().Read(themeRootPath, baseRootPath)!.Sprites.ToDictionary(sprite => sprite.Name);

            Assert.Equal(new AtlasRectangle(0, 0, 2, 2), sprites["s0"].Frame);
            Assert.Equal(new AtlasRectangle(20, 10, 4, 6), sprites["s1"].Frame);
            Assert.Equal(new AtlasPoint(2, 3), sprites["s1"].Pivot);
            Assert.Equal(new AtlasRectangle(0, 20, 8, 8), sprites["extra.png"].Frame);
            Assert.Equal(3, sprites.Count);
        }
        finally
        {
            Directory.Delete(themeRootPath, true);
            Directory.Delete(baseRootPath, true);
        }
    }

    private static string AtlasJson(string id, int spriteCount)
    {
        var sprites = string.Join(",", Enumerable.Range(0, spriteCount).Select(index =>
            $$"""
            "s{{index}}":{"textureFrame":[{{index * 2}},0,2,2],"pivot":[1,1],"sourceSize":[2,2],"sourceOffset":[0,0]}
            """));
        return $$"""{"texture":{"id":"{{id}}","width":32,"height":32},"sprites":{""" + sprites + "}}";
    }

    [Fact]
    public void ReadUsesBaseDefinitionAndThemeImageForDifferentialTheme()
    {
        var themeRootPath = Directory.CreateTempSubdirectory().FullName;
        var baseRootPath = Directory.CreateTempSubdirectory().FullName;
        var themeSpritesPath = Path.Combine(themeRootPath, "resources", "sprites");
        var baseSpritesPath = Path.Combine(baseRootPath, "resources", "sprites");
        Directory.CreateDirectory(themeSpritesPath);
        Directory.CreateDirectory(baseSpritesPath);
        var themeImagePath = Path.Combine(themeSpritesPath, "packed.png");
        File.WriteAllBytes(themeImagePath, [1]);
        File.WriteAllText(
            Path.Combine(baseSpritesPath, "packed.json"),
            """
            {
              "texture": { "id": "base-texture", "width": 512, "height": 1024 },
              "sprites": {}
            }
            """);

        try
        {
            var atlas = new TextureAtlasReader().Read(themeRootPath, baseRootPath);

            Assert.NotNull(atlas);
            Assert.Equal("base-texture", atlas.TextureId);
            Assert.Equal(themeImagePath, atlas.ImagePath);
        }
        finally
        {
            Directory.Delete(themeRootPath, true);
            Directory.Delete(baseRootPath, true);
        }
    }
}
