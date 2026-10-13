namespace MiniThemeStudio.Core.Themes;

public sealed record SpriteReplacementRequest(
    string AtlasImagePath,
    SpriteRegion Sprite,
    string ReplacementImagePath);
