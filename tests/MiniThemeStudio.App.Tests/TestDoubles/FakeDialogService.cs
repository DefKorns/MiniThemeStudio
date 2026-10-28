using MiniThemeStudio.App.Dialogs;

namespace MiniThemeStudio.App.Tests.TestDoubles;

// answers every dialog with the configured result and records what it was asked to show
public sealed class FakeDialogService : IDialogService
{
    public bool ConfirmationAnswer { get; set; } = true;

    public DialogResult ChoiceAnswer { get; set; } = DialogResult.Accepted;

    public List<(string Title, string Message)> Shown { get; } = [];

    public Task<DialogResult> ShowAsync(DialogRequest request)
    {
        Shown.Add((request.Title, request.Message));
        return Task.FromResult(DialogResult.Accepted);
    }

    public Task ShowInformationAsync(string title, string message, DialogVariant variant = DialogVariant.Info)
    {
        Shown.Add((title, message));
        return Task.CompletedTask;
    }

    public Task<bool> ShowConfirmationAsync(
        string title,
        string message,
        DialogVariant variant = DialogVariant.Warning,
        string acceptButtonText = "Continue",
        string cancelButtonText = "Go back",
        bool isDestructive = false)
    {
        Shown.Add((title, message));
        return Task.FromResult(ConfirmationAnswer);
    }

    public Task<DialogResult> ShowChoiceAsync(
        string title,
        string message,
        string acceptButtonText,
        string alternateButtonText,
        string cancelButtonText,
        DialogVariant variant = DialogVariant.Warning)
    {
        Shown.Add((title, message));
        return Task.FromResult(ChoiceAnswer);
    }
}
