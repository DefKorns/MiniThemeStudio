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
    private const string RequiredFieldsMessage = "Name and author are required.";
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
        BaseSelector.SetChoices(NewestPerRegion(_installedBases)
            .Select(installedBase => new BaseChoice(installedBase, installedBase.Definition.DisplayName))
            .ToList());
    }

    // The UI of a console and region is the same in every firmware version, so only the newest one is offered.
    private static List<InstalledBaseInfo> NewestPerRegion(IEnumerable<InstalledBaseInfo> bases)
    {
        return bases
            .GroupBy(installedBase => (installedBase.Definition.Console, installedBase.Definition.Region))
            .Select(group => group
                .OrderByDescending(installedBase => Version.TryParse(installedBase.Definition.Version, out Version? version) ? version : new Version())
                .First())
            .ToList();
    }

    private async void BrowseAtlas_Click(object? sender, RoutedEventArgs e)
    {
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select an existing packed.png to build the theme around",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("PNG image") { Patterns = ["*.png"] }],
        });
        string? imagePath = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (imagePath is null || _atlasLayoutMatcher is null)
        {
            return;
        }

        try
        {
            IReadOnlyList<AtlasLayoutMatch> matches = _atlasLayoutMatcher.Rank(imagePath, _installedBases);
            if (matches.Count == 0)
            {
                ValidationText.Text = "No installed Base has an atlas with the same size as this PNG.";
                return;
            }

            _atlasImagePath = imagePath;
            AtlasPathText.Text = Path.GetFileName(imagePath);
            ClearAtlasButton.IsVisible = true;
            List<AtlasLayoutMatch> bestPerRegion = matches
                .GroupBy(match => (match.Base.Definition.Console, match.Base.Definition.Region))
                .Select(group => group.First())
                .ToList();
            BaseSelector.SetChoices(
                bestPerRegion
                    .Select(match => new BaseChoice(
                        match.Base,
                        string.Create(CultureInfo.InvariantCulture, $"{match.Base.Definition.DisplayName} ({match.CoveragePercent:0.0}% fit)")))
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
        string? name = ThemeNameTextBox.Text?.Trim();
        string? author = AuthorTextBox.Text?.Trim();

        ThemeNameTextBox.Classes.Set("invalid", string.IsNullOrWhiteSpace(name));
        AuthorTextBox.Classes.Set("invalid", string.IsNullOrWhiteSpace(author));
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(author))
        {
            ValidationText.Text = RequiredFieldsMessage;
            return;
        }

        if (BaseSelector.SelectedBase is not { } selectedBase)
        {
            ValidationText.Text = "Select a Base firmware.";
            return;
        }

        Close(new NewThemeDetails(name, author, selectedBase.LocalPath, _atlasImagePath, AdvancedEditingCheckBox.IsChecked == true));
    }

    private void AuthorTextBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(AuthorTextBox.Text))
        {
            AuthorTextBox.Classes.Set("invalid", false);
            ClearRequiredFieldsMessage();
        }
    }

    private void ThemeNameTextBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(ThemeNameTextBox.Text))
        {
            ThemeNameTextBox.Classes.Set("invalid", false);
            ClearRequiredFieldsMessage();
        }

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

    private void ClearRequiredFieldsMessage()
    {
        if (ValidationText.Text == RequiredFieldsMessage
            && !string.IsNullOrWhiteSpace(ThemeNameTextBox.Text)
            && !string.IsNullOrWhiteSpace(AuthorTextBox.Text))
        {
            ValidationText.Text = "";
        }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
