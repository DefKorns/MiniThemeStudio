using System;
using System.Collections.Generic;
using System.IO;
using MiniThemeStudio.Core.Themes;

namespace MiniThemeStudio.App.ViewModels;

public partial class MainWindowViewModel
{
    public IReadOnlyList<SavedDraft> ListSavedDrafts() => _savedDraftStore.List();

    public bool SaveDraft()
    {
        if (_draft is null)
        {
            return false;
        }

        try
        {
            var saved = _savedDraftStore.Save(_draft, ThemeName, _currentBaseUiPath);
            _draft = _draft with { SavedDraftId = saved.Id };
            Status = $"Draft saved at {saved.SavedAt:HH:mm}. Reopen it from Saved drafts.";
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Status = exception.Message;
            return false;
        }
    }

    public bool RestoreDraft(SavedDraft savedDraft)
    {
        ArgumentNullException.ThrowIfNull(savedDraft);
        _currentBaseUiPath = savedDraft.BaseUiPath is { } baseUiPath && Directory.Exists(baseUiPath) ? baseUiPath : null;
        if (!OpenDraft(() => _savedDraftStore.Restore(savedDraft), true))
        {
            return false;
        }

        Status = $"Resumed draft '{savedDraft.Name}' saved at {savedDraft.SavedAt:g}.";
        return true;
    }

    public bool DeleteSavedDraft(SavedDraft savedDraft)
    {
        ArgumentNullException.ThrowIfNull(savedDraft);
        try
        {
            _savedDraftStore.Delete(savedDraft.Id);
            if (_draft?.SavedDraftId == savedDraft.Id)
            {
                _draft = _draft with { SavedDraftId = null };
            }

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Status = exception.Message;
            return false;
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
