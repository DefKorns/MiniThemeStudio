namespace MiniThemeStudio.Core.Themes;

public sealed record ThemeDraft(
    string DraftPath,
    string SavePath,
    bool RequiresEmptySavePath,
    string? LastBackupPath = null,
    string? SavedDraftId = null);
