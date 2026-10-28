using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using MiniThemeStudio.App.Models;
using MiniThemeStudio.Core.Firmware;
using MiniThemeStudio.Core.Settings;
using MiniThemeStudio.Core.Themes;

namespace MiniThemeStudio.App.Services;

// The app's own dialogs, for view models: each returns what the user chose, or null when they cancel.
public interface IAppDialogs
{
    Task<AppSettings?> EditSettingsAsync(AppSettings current, bool isInitialSetup);

    Task ManageBasesAsync();

    Task<NewThemeDetails?> AskNewThemeAsync(IReadOnlyList<InstalledBaseInfo> installedBases);

    Task<InstalledBaseInfo?> SelectBaseAsync(IReadOnlyList<InstalledBaseInfo> installedBases, InstalledBaseInfo? preferred, string message);

    Task<SavedDraft?> PickSavedDraftAsync(Func<IReadOnlyList<SavedDraft>> listDrafts, Func<SavedDraft, Task<bool>> deleteDraft);

    // frames cut from a sprite sheet; referenceFrame is shown next to them for comparison
    Task<SpriteSheetResult?> ImportSpriteSheetAsync(string title, AtlasSize? currentFrameSize, Bitmap? referenceFrame);
}
