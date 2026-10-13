using System.Threading.Tasks;
using Avalonia.Controls;

namespace MiniThemeStudio.App.Dialogs;

public interface IDialogService
{
    Task<DialogResult> ShowAsync(Window owner, DialogRequest request);

    Task ShowInformationAsync(
        Window owner,
        string title,
        string message,
        DialogVariant variant = DialogVariant.Info);

    Task<bool> ShowConfirmationAsync(
        Window owner,
        string title,
        string message,
        DialogVariant variant = DialogVariant.Warning,
        string acceptButtonText = "Continue",
        string cancelButtonText = "Go back",
        bool isDestructive = false);

    Task<DialogResult> ShowChoiceAsync(
        Window owner,
        string title,
        string message,
        string acceptButtonText,
        string alternateButtonText,
        string cancelButtonText,
        DialogVariant variant = DialogVariant.Warning);
}
