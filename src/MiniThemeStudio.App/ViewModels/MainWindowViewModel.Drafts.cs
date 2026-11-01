using System;
using System.Collections.Generic;
using System.IO;
using MiniThemeStudio.Core.Themes;

namespace MiniThemeStudio.App.ViewModels;

public partial class MainWindowViewModel
{
    public IReadOnlyList<SavedDraft> ListSavedDrafts() => _savedDraftStore.List();

    public void SaveDraft()
    {
        if (_session.Draft is null)
        {
            return;
        }

        SavedDraft saved = _savedDraftStore.Save(_session.Draft, ThemeName, _session.BaseUiPath);
        _session.Draft = _session.Draft with { SavedDraftId = saved.Id };
        IsDraftSaved = true;
        Status = $"Draft saved at {saved.SavedAt:HH:mm}. Reopen it from Saved drafts.";
    }

    public void RestoreDraft(SavedDraft savedDraft)
    {
        ArgumentNullException.ThrowIfNull(savedDraft);
        _session.BaseUiPath = savedDraft.BaseUiPath is { } baseUiPath && Directory.Exists(baseUiPath) ? baseUiPath : null;
        OpenDraft(() => _savedDraftStore.Restore(savedDraft), true);
        IsDraftSaved = true;
        Status = $"Resumed draft '{savedDraft.Name}' saved at {savedDraft.SavedAt:g}.";
    }

    public void DeleteSavedDraft(SavedDraft savedDraft)
    {
        ArgumentNullException.ThrowIfNull(savedDraft);
        _savedDraftStore.Delete(savedDraft.Id);
        if (_session.Draft?.SavedDraftId == savedDraft.Id)
        {
            _session.Draft = _session.Draft with { SavedDraftId = null };
        }
    }

    private void ForgetSavedDraftAfterSave()
    {
        if (_session.Draft?.SavedDraftId is not { } id)
        {
            return;
        }

        try
        {
            _savedDraftStore.Delete(id);
            _session.Draft = _session.Draft with { SavedDraftId = null };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Status = $"{Status} (the saved draft could not be removed: {exception.Message})";
        }
    }
}
