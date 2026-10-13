using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using MiniThemeStudio.App.Dialogs;

namespace MiniThemeStudio.App.Errors;

public sealed class ErrorHandlingService(IDialogService dialogService) : IErrorHandlingService
{
    public Task ShowErrorAsync(Window owner, string title, string message)
    {
        return dialogService.ShowInformationAsync(
            owner,
            title,
            message,
            DialogVariant.Error);
    }

    public Task HandleExceptionAsync(Window owner, string title, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return ShowErrorAsync(owner, title, exception.Message);
    }
}
