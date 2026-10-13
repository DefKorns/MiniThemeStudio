namespace MiniThemeStudio.Core.Themes;

public interface ITextureAtlasReader
{
    TextureAtlas? Read(string themeRootPath, string? definitionRootPath = null);
}
