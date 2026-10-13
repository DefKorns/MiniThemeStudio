namespace MiniThemeStudio.Core.Themes;

public sealed record SpriteRegion(
    string Name,
    AtlasRectangle Frame,
    AtlasPoint Pivot,
    AtlasSize SourceSize,
    AtlasPoint SourceOffset);
