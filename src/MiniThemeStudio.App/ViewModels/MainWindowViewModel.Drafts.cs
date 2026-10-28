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
        if (_draft is null)
        {
            return;
        }

        var saved = _savedDraftStore.Save(_draft, ThemeName, _currentBaseUiPath);
        _draft = _draft with { SavedDraftId = saved.Id };
        IsDraftSaved = true;
        Status = $"Draft saved at {saved.SavedAt:HH:mm}. Reopen it from Saved drafts.";
    }

    public void RestoreDraft(SavedDraft savedDraft)
    {
        ArgumentNullException.ThrowIfNull(savedDraft);
        _currentBaseUiPath = savedDraft.BaseUiPath is { } baseUiPath && Directory.Exists(baseUiPath) ? baseUiPath : null;
        OpenDraft(() => _savedDraftStore.Restore(savedDraft), true);
        IsDraftSaved = true;
        Status = $"Resumed draft '{savedDraft.Name}' saved at {savedDraft.SavedAt:g}.";
    }

    public void DeleteSavedDraft(SavedDraft savedDraft)
    {
        ArgumentNullException.ThrowIfNull(savedDraft);
        _savedDraftStore.Delete(savedDraft.Id);
        if (_draft?.SavedDraftId == savedDraft.Id)
        {
            _draft = _draft with { SavedDraftId = null };
        }
    }

    private void ForgetSavedDraftAfterSave()
    {
        if (_draft?.SavedDraftId is not { } id)
        {
            return;
        }

        try
        {
            _savedDraftStore.Delete(id);
            _draft = _draft with { SavedDraftId = null };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Status = $"{Status} (the saved draft could not be removed: {exception.Message})";
        }
    }
}
