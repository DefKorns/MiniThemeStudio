namespace MiniThemeStudio.Core.Themes;

public sealed record TextureAtlas(
    string DefinitionPath,
    string ImagePath,
    string TextureId,
    AtlasSize Size,
    IReadOnlyList<SpriteRegion> Sprites);
