using System;
using System.Globalization;
using System.Linq;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace MiniThemeStudio.App.Converters;

/// <summary>
/// Maps the selected workspace tab index to the checked state of a navigation button. The parameter lists the
/// indexes the button stands for ("0,1,3,4"); checking the button selects the first of them.
/// </summary>
public sealed class TabIndexConverter : IValueConverter
{
    public static TabIndexConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is int index && Parse(parameter).Contains(index);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? Parse(parameter).First() : BindingOperations.DoNothing;
    }

    private static int[] Parse(object? parameter)
    {
        return (parameter?.ToString() ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => int.Parse(part, CultureInfo.InvariantCulture))
            .ToArray();
    }
}
