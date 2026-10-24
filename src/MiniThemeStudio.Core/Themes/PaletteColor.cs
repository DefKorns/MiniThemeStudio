using System;
using System.Globalization;

namespace MiniThemeStudio.Core.Themes;

/// <summary>A named colour of a colors_&lt;region&gt;.lua palette; channels are 0..1, as the engine reads them.</summary>
public readonly record struct PaletteColor(string Name, double Red, double Green, double Blue, double Alpha)
{
    /// <summary>The channel as the firmware's own palettes write it: x/255 with six significant digits.</summary>
    public static double ChannelFromByte(byte value)
    {
        var text = (value / 255.0).ToString("G6", CultureInfo.InvariantCulture);
        return double.Parse(text, CultureInfo.InvariantCulture);
    }

    public static byte ChannelToByte(double value) => (byte)Math.Clamp(Math.Round(value * 255.0), 0, 255);

    public static PaletteColor FromBytes(string name, byte red, byte green, byte blue, byte alpha) =>
        new(name, ChannelFromByte(red), ChannelFromByte(green), ChannelFromByte(blue), ChannelFromByte(alpha));
}
