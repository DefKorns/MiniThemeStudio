using System;
using System.IO;
using System.Net;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MiniThemeStudio.Core.Settings;

namespace MiniThemeStudio.App.Views;

public sealed partial class SettingsDialog : Window
{
    private const string UsbConsoleHost = "169.254.13.37";
    private readonly AppSettings _settings;
    private string _networkConsoleHost;
    private bool _isUpdatingConnectionControls;
    private bool _usesNetwork;
    private bool _isInitialized;

    public SettingsDialog()
        : this(new AppSettings("", "", ApplicationThemePreference.System, ""))
    {
    }

    public SettingsDialog(
        AppSettings settings,
        bool isInitialSetup = false)
    {
        _settings = settings;
        _networkConsoleHost = settings.ConsoleHost == UsbConsoleHost
            ? ""
            : settings.ConsoleHost;
        _usesNetwork = settings.ConsoleConnectionMode == ConsoleConnectionMode.Wifi;
        InitializeComponent();

        ThemeComboBox.ItemsSource = Enum.GetValues<ApplicationThemePreference>();
        DraftRootTextBox.Text = settings.DraftRootPath;
        ThemeDestinationTextBox.Text = settings.DefaultThemeDestinationPath;
        FillFontTextBox.Text = settings.FillFontPath;
        ThemeComboBox.SelectedItem = settings.Theme;
        ConsoleHostTextBox.Text = settings.ConsoleHost;
        UsbConnectionButton.IsChecked = !_usesNetwork;
        NetworkConnectionButton.IsChecked = _usesNetwork;
        UpdateConnectionControls();
        UpdatePathValidation();
        _isInitialized = true;

        if (isInitialSetup)
        {
            TitleText.Text = "Set up Mini Theme Studio";
            SubtitleText.Text = "Choose where drafts are kept and how to reach the console.";
            CancelButton.Content = "Exit";
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

        MarkUnsaved();
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

    private async void BrowseFillFont_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select the fill font",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Fonts") { Patterns = ["*.ttf", "*.otf", "*.ttc"] }],
        });

        FillFontTextBox.Text = files.Count > 0 ? files[0].TryGetLocalPath() ?? FillFontTextBox.Text : FillFontTextBox.Text;
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
        var fillFont = FillFontTextBox.Text?.Trim() ?? "";
        FillFontTextBox.Classes.Set("invalid-path", fillFont.Length > 0 && !File.Exists(fillFont));
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

        Close(_settings with
        {
            DraftRootPath = Path.GetFullPath(DraftRootTextBox.Text.Trim()),
            DefaultThemeDestinationPath = string.IsNullOrWhiteSpace(ThemeDestinationTextBox.Text)
                ? ""
                : Path.GetFullPath(ThemeDestinationTextBox.Text.Trim()),
            Theme = theme,
            FillFontPath = string.IsNullOrWhiteSpace(FillFontTextBox.Text) ? "" : Path.GetFullPath(FillFontTextBox.Text.Trim()),
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
}
