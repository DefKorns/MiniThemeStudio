using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using MiniThemeStudio.App.Diagnostics;
using MiniThemeStudio.App.Dialogs;

namespace MiniThemeStudio.App.Errors;

// Last line of defence: exceptions nothing else handled are logged, and the user is told instead of the app closing silently.
public sealed partial class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, IDialogService dialogService)
{
    private bool _isShowingError;

    public void Register(IClassicDesktopStyleApplicationLifetime desktop)
    {
        ArgumentNullException.ThrowIfNull(desktop);

        // exceptions on the UI thread, including the ones thrown by async void event handlers
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            e.Handled = true;
            LogUiThreadException(logger, e.Exception);
            Notify(desktop.MainWindow, e.Exception);
        };

        // other threads: the runtime still terminates the process, but the cause is logged first
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            LogUnhandledException(logger, e.ExceptionObject as Exception, e.IsTerminating);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            LogUnobservedTaskException(logger, e.Exception);
            e.SetObserved();
        };
    }

    private void Notify(Window? owner, Exception exception)
    {
        // one dialog at a time, so an error raised while showing the dialog cannot loop
        if (_isShowingError || owner is not { IsVisible: true })
        {
            return;
        }

        _isShowingError = true;
        _ = ShowAsync(owner, exception);
    }

    private async Task ShowAsync(Window owner, Exception exception)
    {
        try
        {
            await dialogService.ShowInformationAsync(
                owner,
                "Something went wrong",
                $"{exception.Message}\n\nThe details were written to the log in {FileLogging.LogsDirectory}.",
                DialogVariant.Error);
        }
        catch (Exception dialogException) when (dialogException is not OutOfMemoryException)
        {
            LogDialogFailure(logger, dialogException);
        }
        finally
        {
            _isShowingError = false;
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception on the UI thread")]
    private static partial void LogUiThreadException(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Unhandled exception (terminating: {IsTerminating})")]
    private static partial void LogUnhandledException(ILogger logger, Exception? exception, bool isTerminating);

    [LoggerMessage(Level = LogLevel.Error, Message = "Unobserved task exception")]
    private static partial void LogUnobservedTaskException(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "The error dialog could not be shown")]
    private static partial void LogDialogFailure(ILogger logger, Exception exception);
}
