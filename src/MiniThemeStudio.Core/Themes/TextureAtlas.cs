namespace MiniThemeStudio.Core.Themes;

public sealed record TextureAtlas(
    string DefinitionPath,
    string ImagePath,
    string TextureId,
    AtlasSize Size,
    IReadOnlyList<SpriteRegion> Sprites)
{
    public string Name => Path.GetFileNameWithoutExtension(DefinitionPath);

    public string RelativeImagePath => Path.Combine("resources", "sprites", $"{Name}.png");
}
