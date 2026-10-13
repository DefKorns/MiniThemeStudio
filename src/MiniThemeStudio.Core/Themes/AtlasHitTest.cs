namespace MiniThemeStudio.Core.Themes;

public static class AtlasHitTest
{
    public static SpriteRegion? FindAt(IEnumerable<SpriteRegion> sprites, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(sprites);

        return sprites
            .Where(sprite => x >= sprite.Frame.X
                && y >= sprite.Frame.Y
                && x < sprite.Frame.X + sprite.Frame.Width
                && y < sprite.Frame.Y + sprite.Frame.Height)
            .MinBy(sprite => (long)sprite.Frame.Width * sprite.Frame.Height);
    }
}
