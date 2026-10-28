using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using MiniThemeStudio.App.Dialogs;
using MiniThemeStudio.Core.Firmware;
using MiniThemeStudio.Core.Themes;

namespace MiniThemeStudio.App.ViewModels;

// What the header and the start page do: settings, Bases, drafts, opening, creating, saving and exporting themes.
// Failures go to the error handler, which logs them and tells the user.
public partial class MainWindowViewModel
{
    // first run: settings, a Base, then the offer to resume the last draft; false when the user leaves the setup
    public async Task<bool> StartAsync()
    {
        if (!_settingsStore.Current.InitialSetupCompleted && !await EditSettingsAsync(true))
        {
            return false;
        }

        if (_baseFirmwareManager.FindCompatibleBaseUiPath() is null)
        {
            await _appDialogs.ManageBasesAsync();
        }

        if (!IsThemeOpen
            && ListSavedDrafts() is { Count: > 0 } drafts
            && await _dialogService.ShowConfirmationAsync(
                "Resume your work?",
                $"'{drafts[0].Name}' was saved as a draft at {drafts[0].SavedAt:g}.",
                DialogVariant.Info,
                "Resume draft",
                "Not now"))
        {
            await OpenSavedDraftAsync(drafts[0]);
        }

        return true;
    }

    // before the window closes with unsaved work: offers to keep it as a draft; false keeps the window open
    public async Task<bool> ConfirmCloseAsync()
    {
        if (!IsThemeOpen || !HasUnsavedWork)
        {
            return true;
        }

        var choice = await _dialogService.ShowChoiceAsync(
            "Save draft before exiting?",
            "The open theme has unsaved changes. Save them as a draft to resume next time, or exit and discard them.",
            "Save draft and exit",
            "Exit without saving",
            "Cancel",
            DialogVariant.Info);
        return choice switch
        {
            DialogResult.Cancelled => false,
            DialogResult.Accepted => await RunAsync("Cannot save draft", SaveDraft),
            _ => true,
        };
    }

    [RelayCommand]
    private async Task ShowSettingsAsync()
    {
        await EditSettingsAsync(false);
    }

    [RelayCommand]
    private Task ManageBasesAsync()
    {
        return _appDialogs.ManageBasesAsync();
    }

    [RelayCommand]
    private async Task SaveDraftAsync()
    {
        await RunAsync("Cannot save draft", SaveDraft);
    }

    [RelayCommand]
    private async Task ShowSavedDraftsAsync()
    {
        var draft = await _appDialogs.PickSavedDraftAsync(
            ListSavedDrafts,
            candidate => RunAsync("Cannot delete draft", () => DeleteSavedDraft(candidate)));
        if (draft is not null)
        {
            await OpenSavedDraftAsync(draft);
        }
    }

    [RelayCommand]
    private async Task OpenThemeAsync()
    {
        if (!await ConfirmDiscardDraftAsync() || await _fileDialogs.PickFolderAsync("Edit existing theme") is not { } path)
        {
            return;
        }

        var baseUiPath = _baseFirmwareManager.FindCompatibleBaseUiPath(path);
        if (baseUiPath is null)
        {
            baseUiPath = await AskForBaseAsync(path, "The console of this theme could not be detected. Choose the Base it was made for.");
            if (baseUiPath is null && !HasAtlasDefinition(path))
            {
                return;
            }
        }

        await RunAsync("Cannot open theme", () => OpenTheme(path, baseUiPath));
    }

    [RelayCommand]
    private async Task CreateThemeAsync()
    {
        if (!await ConfirmDiscardDraftAsync()
            || await _appDialogs.AskNewThemeAsync(ValidBases()) is not { } details
            || await GetThemeDestinationParentPathAsync() is not { } destinationParentPath)
        {
            return;
        }

        var request = new NewThemeRequest(
            details.BaseUiPath,
            Path.Combine(destinationParentPath, ThemeDirectoryName.FromDisplayName(details.Name)),
            details.Name,
            details.Author,
            details.AtlasImagePath);
        await RunAsync("Cannot create theme", () => CreateTheme(request));
    }

    [RelayCommand]
    private async Task SaveThemeAsync()
    {
        await RunAsync("Cannot save theme", SaveTheme);
    }

    [RelayCommand]
    private async Task ExportPackageAsync()
    {
        if (_session.Draft is not null && await _fileDialogs.PickFolderAsync("Select the output folder for the .tar.gz package") is { } outputDirectory)
        {
            await RunAsync("Cannot export package", () => ExportPackage(outputDirectory));
        }
    }

    [RelayCommand]
    private async Task ChangeBaseAsync()
    {
        if (_session.Draft is not null
            && await AskForBaseAsync(ThemePath, "Choose the Base this theme is edited against. Changes in the draft are kept.", _session.BaseUiPath) is { } baseUiPath)
        {
            await RunAsync("Cannot change the Base", () => ChangeBase(baseUiPath));
        }
    }

    [RelayCommand]
    private async Task ReturnHomeAsync()
    {
        if (await ConfirmDiscardDraftAsync())
        {
            ShowHome();
        }
    }

    private async Task<bool> EditSettingsAsync(bool isInitialSetup)
    {
        if (await _appDialogs.EditSettingsAsync(_settingsStore.Current, isInitialSetup) is not { } settings)
        {
            return false;
        }

        return await RunAsync("Cannot save settings", () =>
        {
            _settingsStore.Save(settings);
            _appearance.Apply(settings.Theme);
        });
    }

    private async Task OpenSavedDraftAsync(SavedDraft draft)
    {
        if (await ConfirmDiscardDraftAsync())
        {
            await RunAsync("Cannot open draft", () => RestoreDraft(draft));
        }
    }

    private async Task<bool> ConfirmDiscardDraftAsync()
    {
        return !HasUnsavedWork || await _dialogService.ShowConfirmationAsync(
            "Discard draft?",
            "The unsaved changes in this draft will be lost.",
            DialogVariant.Warning,
            "Discard draft",
            "Keep editing",
            true);
    }

    private async Task<string?> AskForBaseAsync(string themePath, string message, string? currentBaseUiPath = null)
    {
        var installedBases = ValidBases();
        if (installedBases.Count == 0)
        {
            await _appDialogs.ManageBasesAsync();
            installedBases = ValidBases();
            if (installedBases.Count == 0)
            {
                return null;
            }
        }

        var preferredPath = currentBaseUiPath ?? _baseFirmwareManager.FindCompatibleBaseUiPath(themePath);
        var preferred = installedBases.FirstOrDefault(info => info.LocalPath == preferredPath);
        return (await _appDialogs.SelectBaseAsync(installedBases, preferred, message))?.LocalPath;
    }

    // the configured destination, created when missing, or a folder the user picks
    private async Task<string?> GetThemeDestinationParentPathAsync()
    {
        var configuredPath = _settingsStore.Current.DefaultThemeDestinationPath;
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return await _fileDialogs.PickFolderAsync("Select destination parent folder");
        }

        return await RunAsync("Cannot use theme destination", () => Directory.CreateDirectory(configuredPath))
            ? configuredPath
            : null;
    }

    private List<InstalledBaseInfo> ValidBases()
    {
        return _baseFirmwareManager.GetInstalledBases()
            .Where(info => info.Status == BaseInstallStatus.Valid)
            .ToList();
    }

    private static bool HasAtlasDefinition(string rootPath)
    {
        return File.Exists(Path.Combine(rootPath, "resources", "sprites", "packed.json"));
    }

    private Task<bool> RunAsync(string failure, Action action)
    {
        return _errorHandler.TryRunAsync(failure, () =>
        {
            action();
            return Task.CompletedTask;
        });
    }
}
