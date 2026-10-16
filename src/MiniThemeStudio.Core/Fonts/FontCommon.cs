namespace MiniThemeStudio.Core.Fonts;

public sealed record FontCommon(
    int LineHeight,
    int Base,
    int ScaleW,
    int ScaleH,
    int Pages,
    bool Packed,
    byte AlphaChannel,
    byte RedChannel,
    byte GreenChannel,
    byte BlueChannel);
