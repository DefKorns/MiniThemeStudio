using System.Threading.Tasks;
using Avalonia.Controls;
using MiniThemeStudio.App.Views;

namespace MiniThemeStudio.App.Dialogs;

public sealed class DialogService : IDialogService
{
    public Task<DialogResult> ShowAsync(Window owner, DialogRequest request)
    {
        return new MessageDialog(request).ShowDialog<DialogResult>(owner);
    }

    public async Task ShowInformationAsync(
        Window owner,
        string title,
        string message,
        DialogVariant variant = DialogVariant.Info)
    {
        await ShowAsync(
            owner,
            new DialogRequest(title, message, variant, DialogCategory.Information));
    }

    public async Task<bool> ShowConfirmationAsync(
        Window owner,
        string title,
        string message,
        DialogVariant variant = DialogVariant.Warning,
        string acceptButtonText = "Continue",
        string cancelButtonText = "Go back",
        bool isDestructive = false)
    {
        var result = await ShowAsync(
            owner,
            new DialogRequest(
                title,
                message,
                variant,
                DialogCategory.Confirmation,
                acceptButtonText,
                cancelButtonText,
                isDestructive));

        return result == DialogResult.Accepted;
    }

    public Task<DialogResult> ShowChoiceAsync(
        Window owner,
        string title,
        string message,
        string acceptButtonText,
        string alternateButtonText,
        string cancelButtonText,
        DialogVariant variant = DialogVariant.Warning)
    {
        return ShowAsync(
            owner,
            new DialogRequest(
                title,
                message,
                variant,
                DialogCategory.Confirmation,
                acceptButtonText,
                cancelButtonText,
                false,
                alternateButtonText));
    }
}
