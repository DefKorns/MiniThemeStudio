using MiniThemeStudio.Core.Themes;

namespace MiniThemeStudio.Core.Tests.Themes;

public sealed class AtlasHitTestTests
{
    private static readonly SpriteRegion Large = Sprite("large", 0, 0, 100, 100);
    private static readonly SpriteRegion Small = Sprite("small", 10, 10, 20, 20);
    private static readonly SpriteRegion Other = Sprite("other", 200, 0, 10, 10);

    [Fact]
    public void ReturnsSmallestSpriteContainingPoint()
    {
        Assert.Equal(Small, AtlasHitTest.FindAt([Large, Small, Other], 15, 15));
        Assert.Equal(Large, AtlasHitTest.FindAt([Large, Small, Other], 50, 50));
    }

    [Fact]
    public void TreatsRightAndBottomEdgesAsOutside()
    {
        Assert.Equal(Other, AtlasHitTest.FindAt([Other], 209.9, 9.9));
        Assert.Null(AtlasHitTest.FindAt([Other], 210, 5));
        Assert.Null(AtlasHitTest.FindAt([Other], 205, 10));
    }

    [Fact]
    public void ReturnsNullOutsideEverySprite()
    {
        Assert.Null(AtlasHitTest.FindAt([Large, Small, Other], 150, 50));
    }

    private static SpriteRegion Sprite(string name, int x, int y, int width, int height)
    {
        return new SpriteRegion(
            name,
            new AtlasRectangle(x, y, width, height),
            new AtlasPoint(width / 2.0, height / 2.0),
            new AtlasSize(width, height),
            new AtlasPoint(0, 0));
    }
}
