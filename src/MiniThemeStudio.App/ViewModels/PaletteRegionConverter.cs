using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace MiniThemeStudio.App.ViewModels;

public sealed class PaletteRegionConverter : IValueConverter
{
    public static PaletteRegionConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string region ? PaletteColorNames.Region(region) : value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
