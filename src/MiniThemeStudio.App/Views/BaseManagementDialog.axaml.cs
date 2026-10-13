using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MiniThemeStudio.App.Dialogs;
using MiniThemeStudio.App.Errors;
using MiniThemeStudio.Core.Firmware;

namespace MiniThemeStudio.App.Views;

public sealed partial class BaseManagementDialog : Window
{
    private readonly IBaseFirmwareManager _manager;
    private readonly ErrorHandlingService _errorService;

    public BaseManagementDialog()
        : this(null!)
    {
    }

    public BaseManagementDialog(IBaseFirmwareManager manager)
    {
        _manager = manager;
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
        StatusText.Text = $"{bases.Count} definitions listed. Bases location: {_manager.BasesRootPath}";
    }

    private void Refresh_Click(object? sender, RoutedEventArgs e)
    {
        RefreshList();
    }

    private async void ImportArchive_Click(object? sender, RoutedEventArgs e)
    {
        if (_manager == null)
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select stock kernel or firmware archive (.tar / .tar.gz)",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Firmware Archives (*.tar.gz; *.tar; *.hsqs)")
                {
                    Patterns = ["*.tar.gz", "*.tar", "*.tgz", "*.hsqs", "*.img"],
                },
            ],
        });

        var archivePath = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (archivePath is null)
        {
            return;
        }

        try
        {
            StatusText.Text = "Extracting and importing Base UI...";
            var result = await _manager.ImportFromArchiveAsync(archivePath);
            RefreshList();
            StatusText.Text = $"Successfully imported {result.Definition.DisplayName}!";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Import failed.";
            await _errorService.ShowErrorAsync(this, "Cannot import archive", ex.Message);
        }
    }

    private async void ImportFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (_manager == null)
        {
            return;
        }

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select local extracted Base UI folder",
            AllowMultiple = false,
        });

        var folderPath = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
        if (folderPath is null)
        {
            return;
        }

        try
        {
            StatusText.Text = "Validating and importing folder...";
            var result = await _manager.ImportFromDirectoryAsync(folderPath);
            RefreshList();
            StatusText.Text = $"Successfully imported {result.Definition.DisplayName}!";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Import failed.";
            await _errorService.ShowErrorAsync(this, "Cannot import folder", ex.Message);
        }
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
