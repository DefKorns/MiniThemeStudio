using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using MiniThemeStudio.App.Models;
using MiniThemeStudio.Core.Firmware;

namespace MiniThemeStudio.App.Controls;

public sealed partial class BaseSelector : UserControl
{
    private IReadOnlyList<BaseChoice> _choices = [];

    public BaseSelector()
    {
        InitializeComponent();
    }

    public InstalledBaseInfo? SelectedBase => (BaseComboBox.SelectedItem as BaseChoice)?.Base;

    public void SetChoices(IReadOnlyList<BaseChoice> choices, InstalledBaseInfo? preferred = null)
    {
        _choices = choices;
        var families = ConsoleFamilies.All
            .Select(family => new FamilyChoice(
                family,
                choices.Any(choice => choice.Family == family)
                    ? ConsoleFamilies.DisplayName(family)
                    : $"{ConsoleFamilies.DisplayName(family)} (not installed)"))
            .ToList();
        FamilyComboBox.ItemsSource = families;

        var preferredChoice = choices.FirstOrDefault(choice => choice.Base.LocalPath == preferred?.LocalPath)
            ?? (choices.Count > 0 ? choices[0] : null);
        FamilyComboBox.SelectedItem = families.FirstOrDefault(family => family.Family == preferredChoice?.Family)
            ?? families[0];
        if (preferredChoice is not null)
        {
            BaseComboBox.SelectedItem = preferredChoice;
        }
    }

    private void FamilyComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (FamilyComboBox.SelectedItem is not FamilyChoice family)
        {
            return;
        }

        var filtered = _choices.Where(choice => choice.Family == family.Family).ToList();
        BaseComboBox.ItemsSource = filtered;
        BaseComboBox.SelectedIndex = filtered.Count > 0 ? 0 : -1;
    }
}
