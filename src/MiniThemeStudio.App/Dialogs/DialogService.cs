using System.Threading.Tasks;
using MiniThemeStudio.App.Services;
using MiniThemeStudio.App.Views;

namespace MiniThemeStudio.App.Dialogs;

public sealed class DialogService : IDialogService
{
    public Task<DialogResult> ShowAsync(DialogRequest request)
    {
        return new MessageDialog(request).ShowDialog<DialogResult>(ActiveWindow.Get());
    }

    public async Task ShowInformationAsync(
        string title,
        string message,
        DialogVariant variant = DialogVariant.Info)
    {
        await ShowAsync(
            new DialogRequest(title, message, variant, DialogCategory.Information));
    }

    public async Task<bool> ShowConfirmationAsync(
        string title,
        string message,
        DialogVariant variant = DialogVariant.Warning,
        string acceptButtonText = "Continue",
        string cancelButtonText = "Go back",
        bool isDestructive = false)
    {
        DialogResult result = await ShowAsync(
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
        string title,
        string message,
        string acceptButtonText,
        string alternateButtonText,
        string cancelButtonText,
        DialogVariant variant = DialogVariant.Warning)
    {
        return ShowAsync(
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
