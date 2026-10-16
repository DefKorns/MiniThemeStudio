namespace MiniThemeStudio.Core.Fonts;

public sealed record FontGlyph(
    uint Id,
    int X,
    int Y,
    int Width,
    int Height,
    int XOffset,
    int YOffset,
    int XAdvance,
    int Page,
    int Channel)
{
    public char Character => Id <= char.MaxValue ? (char)Id : '\uFFFD';
}
