using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MiniThemeStudio.App.Dialogs;
using MiniThemeStudio.App.Errors;
using MiniThemeStudio.Core.Console;
using MiniThemeStudio.Core.Firmware;
using MiniThemeStudio.Core.Settings;

namespace MiniThemeStudio.App.Views;

public sealed partial class BaseManagementDialog : Window
{
    private const string UsbConsoleHost = "169.254.13.37";

    private readonly IBaseFirmwareManager _manager;
    private readonly AppSettings? _settings;
    private readonly ErrorHandlingService _errorService;

    public BaseManagementDialog()
        : this(null!, null)
    {
    }

    public BaseManagementDialog(IBaseFirmwareManager manager, AppSettings? settings)
    {
        _manager = manager;
        _settings = settings;
        _errorService = new ErrorHandlingService(new DialogService());
        InitializeComponent();
        Loaded += (_, _) => RefreshList();
    }

    private void RefreshList()
    {
        if (_manager == null)
        {
            return;
        }

        var bases = _manager.GetInstalledBases();
        BasesListBox.ItemsSource = bases;
        var installedCount = bases.Count(info => info.Status == BaseInstallStatus.Valid);
        StatusText.Text = $"{installedCount} of {bases.Count} bases installed in {_manager.BasesRootPath}";
    }

    private void Refresh_Click(object? sender, RoutedEventArgs e)
    {
        RefreshList();
    }

    private async void ImportFile_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select original firmware image (.hsqs, .tar, .tar.gz)",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Firmware images") { Patterns = ["*.hsqs", "*.tar.gz", "*.tar", "*.tgz"] },
                new FilePickerFileType("All files") { Patterns = ["*"] },
            ],
        });

        var filePath = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (filePath is not null)
        {
            await RunImportAsync("Verifying and extracting firmware...", "Cannot import firmware", () => _manager.ImportFromFileAsync(filePath));
        }
    }

    private async void ImportFolder_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select an extracted Base UI folder (containing init.json)",
            AllowMultiple = false,
        });

        var folderPath = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
        if (folderPath is not null)
        {
            await RunImportAsync("Verifying folder...", "Cannot import folder", () => _manager.ImportFromDirectoryAsync(folderPath));
        }
    }

    private async void ImportConsole_Click(object? sender, RoutedEventArgs e)
    {
        var host = _settings is { ConsoleConnectionMode: ConsoleConnectionMode.Wifi }
            ? _settings.ConsoleHost
            : UsbConsoleHost;
        using var progressDialog = new ConsoleConnectionDialog(cancellationToken =>
            _manager.ImportFromConsoleAsync(new ConsoleConnectionOptions(host), cancellationToken));
        await progressDialog.ShowDialog<bool>(this);

        if (progressDialog.Result is { } result)
        {
            RefreshList();
            StatusText.Text = $"Installed {result.Definition.DisplayName} v{result.Definition.Version}.";
        }
        else if (progressDialog.Error is { } error)
        {
            await _errorService.ShowErrorAsync(this, "Cannot import from console", error.Message);
        }
    }

    private async Task RunImportAsync(string progressText, string errorTitle, Func<Task<InstalledBaseInfo>> import)
    {
        ImportButtons.IsEnabled = false;
        StatusText.Text = progressText;
        try
        {
            var result = await import();
            RefreshList();
            StatusText.Text = $"Installed {result.Definition.DisplayName} v{result.Definition.Version}.";
        }
        catch (Exception exception)
        {
            StatusText.Text = "Import failed.";
            await _errorService.ShowErrorAsync(this, errorTitle, exception.Message);
        }
        finally
        {
            ImportButtons.IsEnabled = true;
        }
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
