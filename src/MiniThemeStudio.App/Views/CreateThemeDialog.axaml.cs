using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using MiniThemeStudio.App.Models;
using MiniThemeStudio.Core.Themes;

namespace MiniThemeStudio.App.Views;

public sealed partial class CreateThemeDialog : Window
{
    public CreateThemeDialog()
    {
        InitializeComponent();
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

        Close(new NewThemeDetails(name, author));
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
