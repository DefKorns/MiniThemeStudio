using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using MiniThemeStudio.App.Models;
using MiniThemeStudio.Core.Firmware;
using MiniThemeStudio.Core.Themes;

namespace MiniThemeStudio.App.Views;

public sealed partial class CreateThemeDialog : Window
{
    public CreateThemeDialog()
        : this([])
    {
    }

    public CreateThemeDialog(IReadOnlyList<InstalledBaseInfo> installedBases)
    {
        InitializeComponent();
        BaseComboBox.ItemsSource = installedBases;
        BaseComboBox.SelectedIndex = installedBases.Count > 0 ? 0 : -1;
        if (installedBases.Count == 0)
        {
            ValidationText.Text = "Install a Base firmware first (Manage Base Firmwares).";
        }
    }

    private void Continue_Click(object? sender, RoutedEventArgs e)
    {
        var name = ThemeNameTextBox.Text?.Trim();
        var author = AuthorTextBox.Text?.Trim();

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(author))
        {
            ValidationText.Text = "Name and author are required.";
            return;
        }

        if (BaseComboBox.SelectedItem is not InstalledBaseInfo selectedBase)
        {
            ValidationText.Text = "Select a Base firmware.";
            return;
        }

        Close(new NewThemeDetails(name, author, selectedBase.LocalPath));
    }

    private void ThemeNameTextBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        try
        {
            DirectoryNameText.Text = string.IsNullOrWhiteSpace(ThemeNameTextBox.Text)
                ? ""
                : ThemeDirectoryName.FromDisplayName(ThemeNameTextBox.Text);
        }
        catch (ArgumentException)
        {
            DirectoryNameText.Text = "";
        }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
