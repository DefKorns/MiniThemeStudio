using System.Threading.Tasks;

namespace MiniThemeStudio.App.Dialogs;

public interface IDialogService
{
    Task<DialogResult> ShowAsync(DialogRequest request);

    Task ShowInformationAsync(
        string title,
        string message,
        DialogVariant variant = DialogVariant.Info);

    Task<bool> ShowConfirmationAsync(
        string title,
        string message,
        DialogVariant variant = DialogVariant.Warning,
        string acceptButtonText = "Continue",
        string cancelButtonText = "Go back",
        bool isDestructive = false);

    Task<DialogResult> ShowChoiceAsync(
        string title,
        string message,
        string acceptButtonText,
        string alternateButtonText,
        string cancelButtonText,
        DialogVariant variant = DialogVariant.Warning);
}
