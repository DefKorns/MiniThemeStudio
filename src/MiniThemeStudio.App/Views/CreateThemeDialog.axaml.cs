using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MiniThemeStudio.App.Models;
using MiniThemeStudio.Core.Firmware;
using MiniThemeStudio.Core.Themes;

namespace MiniThemeStudio.App.Views;

public sealed partial class CreateThemeDialog : Window
{
    private readonly IReadOnlyList<InstalledBaseInfo> _installedBases;
    private readonly IAtlasLayoutMatcher? _atlasLayoutMatcher;
    private string? _atlasImagePath;

    public CreateThemeDialog()
        : this([], null)
    {
    }

    public CreateThemeDialog(IReadOnlyList<InstalledBaseInfo> installedBases, IAtlasLayoutMatcher? atlasLayoutMatcher)
    {
        _installedBases = installedBases;
        _atlasLayoutMatcher = atlasLayoutMatcher;
        InitializeComponent();
        BrowseAtlasButton.IsEnabled = atlasLayoutMatcher is not null && installedBases.Count > 0;
        ShowAllBases();
        if (installedBases.Count == 0)
        {
            ValidationText.Text = "Install a Base firmware first (Manage Base Firmwares).";
        }
    }

    private void ShowAllBases()
    {
        BaseSelector.SetChoices(_installedBases
            .Select(installedBase => new BaseChoice(installedBase, $"{installedBase.Definition.DisplayName} v{installedBase.Definition.Version}"))
            .ToList());
    }

    private async void BrowseAtlas_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select an existing packed.png to build the theme around",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("PNG image") { Patterns = ["*.png"] }],
        });
        var imagePath = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (imagePath is null || _atlasLayoutMatcher is null)
        {
            return;
        }

        try
        {
            var matches = _atlasLayoutMatcher.Rank(imagePath, _installedBases);
            if (matches.Count == 0)
            {
                ValidationText.Text = "No installed Base has an atlas with the same size as this PNG.";
                return;
            }

            _atlasImagePath = imagePath;
            AtlasPathText.Text = Path.GetFileName(imagePath);
            ClearAtlasButton.IsVisible = true;
            BaseSelector.SetChoices(
                matches
                    .Select(match => new BaseChoice(
                        match.Base,
                        string.Create(CultureInfo.InvariantCulture, $"{match.Base.Definition.DisplayName} v{match.Base.Definition.Version} ({match.CoveragePercent:0.0}% fit)")))
                    .ToList(),
                matches[0].Base);
            ValidationText.Text = matches[0].CoveragePercent < 90
                ? "Low fit: this PNG may not be laid out for any installed Base."
                : "";
        }
        catch (InvalidDataException exception)
        {
            ValidationText.Text = exception.Message;
        }
    }

    private void ClearAtlas_Click(object? sender, RoutedEventArgs e)
    {
        _atlasImagePath = null;
        AtlasPathText.Text = "Use the Base atlas";
        ClearAtlasButton.IsVisible = false;
        ValidationText.Text = "";
        ShowAllBases();
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

        if (BaseSelector.SelectedBase is not { } selectedBase)
        {
            ValidationText.Text = "Select a Base firmware.";
            return;
        }

        Close(new NewThemeDetails(name, author, selectedBase.LocalPath, _atlasImagePath));
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
