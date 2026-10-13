using System;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace MiniThemeStudio.App.Errors;

public interface IErrorHandlingService
{
    Task ShowErrorAsync(Window owner, string title, string message);

    Task HandleExceptionAsync(Window owner, string title, Exception exception);
}
