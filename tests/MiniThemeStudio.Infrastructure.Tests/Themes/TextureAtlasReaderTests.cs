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
