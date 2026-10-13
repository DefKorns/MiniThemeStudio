namespace MiniThemeStudio.Core.Themes;

public interface ITextureAtlasReader
{
    TextureAtlas? Read(string themeRootPath, string? definitionRootPath = null);

    IReadOnlyList<TextureAtlas> ReadAll(string themeRootPath, string? definitionRootPath = null);
}
