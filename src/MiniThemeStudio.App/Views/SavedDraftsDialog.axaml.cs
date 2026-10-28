using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MiniThemeStudio.App.Dialogs;
using MiniThemeStudio.Core.Themes;

namespace MiniThemeStudio.App.Views;

public sealed partial class SavedDraftsDialog : Window
{
    private readonly Func<IReadOnlyList<SavedDraft>> _listDrafts;
    private readonly Func<SavedDraft, Task<bool>> _deleteDraft;
    private readonly IDialogService _dialogService;

    // for the XAML previewer only
    public SavedDraftsDialog()
        : this(() => [], _ => Task.FromResult(false), null!)
    {
    }

    public SavedDraftsDialog(Func<IReadOnlyList<SavedDraft>> listDrafts, Func<SavedDraft, Task<bool>> deleteDraft, IDialogService dialogService)
    {
        _listDrafts = listDrafts;
        _deleteDraft = deleteDraft;
        _dialogService = dialogService;
        InitializeComponent();
        DraftsListBox.SelectionChanged += (_, _) =>
        {
            bool hasSelection = DraftsListBox.SelectedItem is SavedDraft;
            OpenButton.IsEnabled = hasSelection;
            DeleteButton.IsEnabled = hasSelection;
        };
        Loaded += (_, _) => Refresh();
    }

    private void Refresh()
    {
        IReadOnlyList<SavedDraft> drafts = _listDrafts();
        DraftsListBox.ItemsSource = drafts;
        DraftsListBox.SelectedIndex = drafts.Count > 0 ? 0 : -1;
        StatusText.Text = drafts.Count == 0 ? "No saved drafts yet. Use Save draft in the header." : "";
    }

    private void Open_Click(object? sender, RoutedEventArgs e)
    {
        if (DraftsListBox.SelectedItem is SavedDraft draft)
        {
            Close(draft);
        }
    }

    private void DraftsListBox_DoubleTapped(object? sender, TappedEventArgs e)
    {
        Open_Click(sender, e);
    }

    private async void Delete_Click(object? sender, RoutedEventArgs e)
    {
        if (DraftsListBox.SelectedItem is not SavedDraft draft)
        {
            return;
        }

        bool confirmed = await _dialogService.ShowConfirmationAsync(
            "Delete saved draft?",
            $"'{draft.Name}' saved at {draft.SavedAt:g} will be removed. The theme folder itself is not touched.",
            DialogVariant.Warning,
            "Delete draft",
            "Cancel",
            true);
        if (confirmed && await _deleteDraft(draft))
        {
            Refresh();
        }
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
