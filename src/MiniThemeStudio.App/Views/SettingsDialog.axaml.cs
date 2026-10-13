using System;
using System.IO;
using System.Linq;
using System.Net;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MiniThemeStudio.Core.Console;
using MiniThemeStudio.Core.Settings;
using MiniThemeStudio.Core.Themes;

namespace MiniThemeStudio.App.Views;

public sealed partial class SettingsDialog : Window
{
    private const string UsbConsoleHost = "169.254.13.37";
    private readonly AppSettings _settings;
    private readonly IConsoleBaseUiImporter? _consoleBaseUiImporter;
    private readonly IBaseUiValidator? _baseUiValidator;
    private readonly ILocalBaseUiImporter? _localBaseUiImporter;
    private readonly string _applicationBaseUiRootPath;
    private string _networkConsoleHost;
    private bool _isUpdatingConnectionControls;
    private bool _usesNetwork;
    private bool _isInitialized;

    public SettingsDialog()
        : this(new AppSettings("", "", "", ApplicationThemePreference.System, ""), null, null, null)
    {
    }

    public SettingsDialog(
        AppSettings settings,
        IConsoleBaseUiImporter? consoleBaseUiImporter = null,
        IBaseUiValidator? baseUiValidator = null,
        ILocalBaseUiImporter? localBaseUiImporter = null,
        bool isInitialSetup = false)
    {
        _settings = settings;
        _consoleBaseUiImporter = consoleBaseUiImporter;
        _baseUiValidator = baseUiValidator;
        _localBaseUiImporter = localBaseUiImporter;
        _networkConsoleHost = settings.ConsoleHost == UsbConsoleHost
            ? ""
            : settings.ConsoleHost;
        _usesNetwork = settings.ConsoleConnectionMode == ConsoleConnectionMode.Wifi;
        _applicationBaseUiRootPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MiniThemeStudio",
            "base-ui");
        InitializeComponent();

        ThemeComboBox.ItemsSource = Enum.GetValues<ApplicationThemePreference>();
        DraftRootTextBox.Text = settings.DraftRootPath;
        ThemeDestinationTextBox.Text = settings.DefaultThemeDestinationPath;
        ThemeComboBox.SelectedItem = settings.Theme;
        ConsoleHostTextBox.Text = settings.ConsoleHost;
        BrowseBaseUiButton.IsEnabled = localBaseUiImporter is not null;
        UsbConnectionButton.IsChecked = !_usesNetwork;
        NetworkConnectionButton.IsChecked = _usesNetwork;
        UpdateConnectionControls();
        UpdatePathValidation();
        _isInitialized = true;

        if (isInitialSetup)
        {
            TitleText.Text = "Set up Mini Theme Studio";
            SubtitleText.Text = "Import or select an original Base UI before editing themes.";
            CancelButton.Content = "Exit";
        }
    }

    private async void BrowseBaseUiRoot_Click(object? sender, RoutedEventArgs e)
    {
        var sourcePath = await PickFolderAsync("Select local Base UI folder");
        if (sourcePath is null || _localBaseUiImporter is null)
        {
            return;
        }

        try
        {
            var importedPath = _localBaseUiImporter.Import(
                sourcePath,
                _applicationBaseUiRootPath);
            SetFeedback($"Imported {Path.GetFileName(importedPath)}.", false);
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or ArgumentException)
        {
            SetFeedback(exception.Message, true);
        }
    }

    private async void GetFromConsole_Click(object? sender, RoutedEventArgs e)
    {
        if (_consoleBaseUiImporter is null
            || !TryGetConsoleHost(out var consoleHost))
        {
            SetFeedback("Enter a valid network IP address first.", true);
            return;
        }

        var connection = new ConsoleConnectionOptions(consoleHost);
        using var progressDialog = new ConsoleConnectionDialog(cancellationToken =>
            _consoleBaseUiImporter.ImportAvailableAsync(
                _applicationBaseUiRootPath,
                connection,
                cancellationToken));
        await progressDialog.ShowDialog<bool>(this);

        if (progressDialog.Result is { } result)
        {
            SetFeedback($"Imported {result.Definition.DisplayName}.", false);
            return;
        }

        if (progressDialog.Error is null)
        {
            return;
        }

        SetFeedback("Console unavailable. Select a local Base UI folder.", true);
        var localPath = await PickFolderAsync("Select local Base UI folder");
        if (localPath is not null && _localBaseUiImporter is not null)
        {
            try
            {
                var importedPath = _localBaseUiImporter.Import(
                    localPath,
                    _applicationBaseUiRootPath);
                SetFeedback($"Imported {Path.GetFileName(importedPath)}.", false);
            }
            catch (Exception exception) when (
                exception is IOException or InvalidDataException or ArgumentException)
            {
                SetFeedback(exception.Message, true);
            }
        }
    }

    private void ConnectionMode_Click(object? sender, RoutedEventArgs e)
    {
        _usesNetwork = sender == NetworkConnectionButton;
        UsbConnectionButton.IsChecked = !_usesNetwork;
        NetworkConnectionButton.IsChecked = _usesNetwork;

        UpdateConnectionControls();
        MarkUnsaved();
    }

    private void ConsoleHostTextBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (!_isUpdatingConnectionControls
            && _usesNetwork)
        {
            _networkConsoleHost = ConsoleHostTextBox.Text?.Trim() ?? "";
        }

        UpdateGetFromConsoleButton();
        MarkUnsaved();
    }

    private void UpdateGetFromConsoleButton()
    {
        GetFromConsoleButton.IsEnabled = _consoleBaseUiImporter is not null
            && TryGetConsoleHost(out _);
    }

    private void UpdateConnectionControls()
    {
        if (!_usesNetwork
            && ConsoleHostTextBox.IsEnabled
            && ConsoleHostTextBox.Text is { } currentHost
            && currentHost != UsbConsoleHost)
        {
            _networkConsoleHost = currentHost.Trim();
        }

        _isUpdatingConnectionControls = true;
        try
        {
            ConsoleHostTextBox.Text = _usesNetwork
                ? _networkConsoleHost
                : UsbConsoleHost;
            ConsoleHostTextBox.IsEnabled = _usesNetwork;
            ConnectionHintText.Text = _usesNetwork
                ? "Network SSH uses root on port 22 without a password."
                : $"USB uses {UsbConsoleHost} with root on port 22.";
            UpdateGetFromConsoleButton();
        }
        finally
        {
            _isUpdatingConnectionControls = false;
        }
    }

    private bool TryGetConsoleHost(out string host)
    {
        if (!_usesNetwork)
        {
            host = UsbConsoleHost;
            return true;
        }

        host = ConsoleHostTextBox.Text?.Trim() ?? "";
        return IPAddress.TryParse(host, out _);
    }

    private async void BrowseDraftRoot_Click(object? sender, RoutedEventArgs e)
    {
        DraftRootTextBox.Text = await PickFolderAsync("Select draft root")
            ?? DraftRootTextBox.Text;
    }

    private async void BrowseThemeDestination_Click(object? sender, RoutedEventArgs e)
    {
        ThemeDestinationTextBox.Text = await PickFolderAsync("Select default theme destination")
            ?? ThemeDestinationTextBox.Text;
    }

    private void PathTextBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        UpdatePathValidation();
        MarkUnsaved();
    }

    private void ThemeComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        MarkUnsaved();
    }

    private void UpdatePathValidation()
    {
        UpdatePathValidation(DraftRootTextBox, false);
        UpdatePathValidation(ThemeDestinationTextBox, true);
    }

    private static void UpdatePathValidation(TextBox textBox, bool allowsEmpty)
    {
        var path = textBox.Text?.Trim() ?? "";
        var isValid = (allowsEmpty && path.Length == 0) || Directory.Exists(path);
        textBox.Classes.Set("invalid-path", !isValid);
        ToolTip.SetTip(textBox, path.Length == 0 ? null : path);
    }

    private void MarkUnsaved()
    {
        if (_isInitialized)
        {
            UnsavedChangesText.IsVisible = true;
        }
    }

    private void SetFeedback(string message, bool isError)
    {
        ValidationText.Text = message;
        ValidationText.Classes.Set("error", isError);
        ValidationText.Classes.Set("success", !isError);
    }

    private async System.Threading.Tasks.Task<string?> PickFolderAsync(string title)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });

        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(DraftRootTextBox.Text)
            || ThemeComboBox.SelectedItem is not ApplicationThemePreference theme)
        {
            SetFeedback("Complete all required fields.", true);
            return;
        }

        if (!TryGetConsoleHost(out var consoleHost))
        {
            SetFeedback("Enter a valid network IP address.", true);
            return;
        }

        var baseUiRootPath = _applicationBaseUiRootPath;
        if (_baseUiValidator is not null && !ContainsValidBaseUi(baseUiRootPath))
        {
            SetFeedback("Import or select a valid Base UI before saving.", true);
            return;
        }

        Close(_settings with
        {
            BaseUiRootPath = baseUiRootPath,
            DraftRootPath = Path.GetFullPath(DraftRootTextBox.Text.Trim()),
            DefaultThemeDestinationPath = string.IsNullOrWhiteSpace(ThemeDestinationTextBox.Text)
                ? ""
                : Path.GetFullPath(ThemeDestinationTextBox.Text.Trim()),
            Theme = theme,
            ConsoleHost = IPAddress.TryParse(_networkConsoleHost, out _)
                ? _networkConsoleHost
                : consoleHost,
            InitialSetupCompleted = true,
            ConsoleConnectionMode = _usesNetwork
                ? ConsoleConnectionMode.Wifi
                : ConsoleConnectionMode.Usb,
        });
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }

    private bool ContainsValidBaseUi(string rootPath)
    {
        if (IsValidBaseUi(rootPath))
        {
            return true;
        }

        foreach (var definition in BaseUiCatalog.All)
        {
            if (IsValidBaseUi(Path.Combine(rootPath, definition.LocalFolderName)))
            {
                return true;
            }
        }

        return Directory.Exists(rootPath)
            && Directory.EnumerateDirectories(rootPath).Any(IsValidBaseUi);
    }

    private bool IsValidBaseUi(string path)
    {
        try
        {
            _baseUiValidator!.Validate(path);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or ArgumentException)
        {
            return false;
        }
    }
}
