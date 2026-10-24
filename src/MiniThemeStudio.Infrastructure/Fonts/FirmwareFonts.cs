using System;

namespace MiniThemeStudio.Infrastructure.Fonts;

internal static class FirmwareFonts
{
    // the firmware's own: the copyright text never changes and debug is never shown
    public static bool IsFirmwareOnly(string fontName) =>
        fontName.Equals("debug", StringComparison.OrdinalIgnoreCase) || fontName.Equals("copyright", StringComparison.OrdinalIgnoreCase);
}
