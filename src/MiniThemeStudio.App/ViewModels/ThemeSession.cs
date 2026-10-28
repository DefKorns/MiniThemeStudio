using System;
using CommunityToolkit.Mvvm.ComponentModel;
using MiniThemeStudio.Core.Themes;

namespace MiniThemeStudio.App.ViewModels;

// The theme being edited, shared by the window and the tab view models: its draft, the Base it is edited against,
// whether it has unsaved changes and the status line.
public sealed partial class ThemeSession(IThemeResourceIndexer resourceIndexer) : ObservableObject
{
    [ObservableProperty]
    private ThemeDraft? draft;

    [ObservableProperty]
    private string? baseUiPath;

    [ObservableProperty]
    private string status = "Ready";

    [ObservableProperty]
    private bool hasUnsavedChanges;

    // the changes not yet saved to the theme are kept in a saved draft
    [ObservableProperty]
    private bool isDraftSaved;

    // which scenes use which textures, refreshed by Reload
    public ThemeResourceReport Report { get; private set; } = ThemeResourceReport.Empty;

    public string? DraftPath => Draft?.DraftPath;

    // the theme was opened, closed or changed on disk: every tab reloads what it shows
    public event EventHandler? Reloaded;

    // a tab wrote to the draft: the window reloads the theme and validates it again
    public event EventHandler? ContentChanged;

    public void MarkChanged()
    {
        HasUnsavedChanges = true;
        IsDraftSaved = false;
    }

    public void NotifyContentChanged()
    {
        MarkChanged();
        ContentChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Reload()
    {
        Report = Draft is null ? ThemeResourceReport.Empty : resourceIndexer.Analyze(Draft.DraftPath, BaseUiPath);
        Reloaded?.Invoke(this, EventArgs.Empty);
    }
}
