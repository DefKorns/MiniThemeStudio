using Avalonia.Media.Imaging;
using MiniThemeStudio.App.Models;
using MiniThemeStudio.App.Services;
using MiniThemeStudio.Core.Firmware;
using MiniThemeStudio.Core.Settings;
using MiniThemeStudio.Core.Themes;

namespace MiniThemeStudio.App.Tests.TestDoubles;

// every dialog is cancelled unless a test sets what it returns
public sealed class FakeAppDialogs : IAppDialogs
{
    public AppSettings? Settings { get; set; }

    public NewThemeDetails? NewTheme { get; set; }

    public InstalledBaseInfo? Base { get; set; }

    public SavedDraft? SavedDraft { get; set; }

    public int ManageBasesCalls { get; private set; }

    public Task<AppSettings?> EditSettingsAsync(AppSettings current, bool isInitialSetup) => Task.FromResult(Settings);

    public Task ManageBasesAsync()
    {
        ManageBasesCalls++;
        return Task.CompletedTask;
    }

    public Task<NewThemeDetails?> AskNewThemeAsync(IReadOnlyList<InstalledBaseInfo> installedBases) => Task.FromResult(NewTheme);

    public Task<InstalledBaseInfo?> SelectBaseAsync(IReadOnlyList<InstalledBaseInfo> installedBases, InstalledBaseInfo? preferred, string message) =>
        Task.FromResult(Base);

    public Task<SavedDraft?> PickSavedDraftAsync(Func<IReadOnlyList<SavedDraft>> listDrafts, Func<SavedDraft, Task<bool>> deleteDraft) =>
        Task.FromResult(SavedDraft);

    public Task<SpriteSheetResult?> ImportSpriteSheetAsync(string title, AtlasSize? currentFrameSize, Bitmap? referenceFrame) =>
        Task.FromResult<SpriteSheetResult?>(null);
}
