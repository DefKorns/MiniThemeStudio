using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MiniThemeStudio.App.Dialogs;
using MiniThemeStudio.App.ViewModels;
using MiniThemeStudio.Core.Themes;

namespace MiniThemeStudio.App.Views;

public sealed partial class SavedDraftsDialog : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly DialogService _dialogService = new();

    public SavedDraftsDialog()
        : this(null!)
    {
    }

    public SavedDraftsDialog(MainWindowViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DraftsListBox.SelectionChanged += (_, _) =>
        {
            var hasSelection = DraftsListBox.SelectedItem is SavedDraft;
            OpenButton.IsEnabled = hasSelection;
            DeleteButton.IsEnabled = hasSelection;
        };
        Loaded += (_, _) => Refresh();
    }

    private void Refresh()
    {
        var drafts = _viewModel.ListSavedDrafts();
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

        var confirmed = await _dialogService.ShowConfirmationAsync(
            this,
            "Delete saved draft?",
            $"'{draft.Name}' saved at {draft.SavedAt:g} will be removed. The theme folder itself is not touched.",
            DialogVariant.Warning,
            "Delete draft",
            "Cancel",
            true);
        if (confirmed && !_viewModel.DeleteSavedDraft(draft))
        {
            StatusText.Text = _viewModel.Status;
            return;
        }

        Refresh();
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
