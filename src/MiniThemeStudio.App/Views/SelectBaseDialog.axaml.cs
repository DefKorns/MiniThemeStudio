using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using MiniThemeStudio.App.Models;
using MiniThemeStudio.Core.Firmware;

namespace MiniThemeStudio.App.Views;

public sealed partial class SelectBaseDialog : Window
{
    public SelectBaseDialog()
        : this([], null, "")
    {
    }

    public SelectBaseDialog(IReadOnlyList<InstalledBaseInfo> installedBases, InstalledBaseInfo? current, string message)
    {
        InitializeComponent();
        MessageText.Text = message;
        BaseSelector.SetChoices(
            installedBases
                .Select(installedBase => new BaseChoice(installedBase, $"{installedBase.Definition.DisplayName} v{installedBase.Definition.Version}"))
                .ToList(),
            current);
    }

    private void Use_Click(object? sender, RoutedEventArgs e)
    {
        if (BaseSelector.SelectedBase is { } selectedBase)
        {
            Close(selectedBase);
        }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
