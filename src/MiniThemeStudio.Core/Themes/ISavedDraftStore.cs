namespace MiniThemeStudio.Core.Themes;

public sealed record SavedDraft(
    string Id,
    string Name,
    string SavePath,
    bool RequiresEmptySavePath,
    string? BaseUiPath,
    DateTimeOffset SavedAt,
    string SnapshotPath);

public interface ISavedDraftStore
{
    IReadOnlyList<SavedDraft> List();

    SavedDraft Save(ThemeDraft draft, string name, string? baseUiPath);

    ThemeDraft Restore(SavedDraft savedDraft);

    void Delete(string id);
}
