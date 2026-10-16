namespace MiniThemeStudio.Core.Fonts;

public sealed record FontInfo(
    string FontName,
    short FontSize,
    bool Bold,
    bool Italic,
    bool Unicode,
    bool Smooth,
    byte Charset,
    ushort StretchH,
    byte Aa,
    byte PaddingUp,
    byte PaddingRight,
    byte PaddingDown,
    byte PaddingLeft,
    byte SpacingHoriz,
    byte SpacingVert,
    byte Outline);
