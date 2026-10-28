using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MiniThemeStudio.App.Dialogs;
using MiniThemeStudio.App.Errors;
using MiniThemeStudio.App.Models;
using MiniThemeStudio.App.Services;
using MiniThemeStudio.Core.Firmware;
using MiniThemeStudio.Core.Settings;
using MiniThemeStudio.Core.Themes;

namespace MiniThemeStudio.App.Views;

public sealed class AppDialogs(
    IBaseFirmwareManager baseFirmwareManager,
    IAppSettingsStore settingsStore,
    IAtlasLayoutMatcher atlasLayoutMatcher,
    IDialogService dialogService,
    IErrorHandler errorHandler) : IAppDialogs
{
    public Task<AppSettings?> EditSettingsAsync(AppSettings current, bool isInitialSetup)
    {
        return new SettingsDialog(current, isInitialSetup).ShowDialog<AppSettings?>(ActiveWindow.Get());
    }

    public Task ManageBasesAsync()
    {
        return new BaseManagementDialog(baseFirmwareManager, settingsStore.Current, errorHandler).ShowDialog(ActiveWindow.Get());
    }

    public Task<NewThemeDetails?> AskNewThemeAsync(IReadOnlyList<InstalledBaseInfo> installedBases)
    {
        return new CreateThemeDialog(installedBases, atlasLayoutMatcher).ShowDialog<NewThemeDetails?>(ActiveWindow.Get());
    }

    public Task<InstalledBaseInfo?> SelectBaseAsync(IReadOnlyList<InstalledBaseInfo> installedBases, InstalledBaseInfo? preferred, string message)
    {
        return new SelectBaseDialog(installedBases, preferred, message).ShowDialog<InstalledBaseInfo?>(ActiveWindow.Get());
    }

    public Task<SavedDraft?> PickSavedDraftAsync(Func<IReadOnlyList<SavedDraft>> listDrafts, Func<SavedDraft, Task<bool>> deleteDraft)
    {
        return new SavedDraftsDialog(listDrafts, deleteDraft, dialogService).ShowDialog<SavedDraft?>(ActiveWindow.Get());
    }
}
