using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;

namespace MiniThemeStudio.App.Errors;

// Last line of defence for exceptions nothing else handled: they are logged, and on the UI thread the user is told
// instead of the app closing silently.
public sealed partial class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, IErrorHandler errorHandler)
{
    public void Register()
    {
        // exceptions on the UI thread, including the ones thrown by async void event handlers
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            e.Handled = true;
            _ = errorHandler.HandleAsync(e.Exception, "Something went wrong");
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

    [LoggerMessage(Level = LogLevel.Critical, Message = "Unhandled exception (terminating: {IsTerminating})")]
    private static partial void LogUnhandledException(ILogger logger, Exception? exception, bool isTerminating);

    [LoggerMessage(Level = LogLevel.Error, Message = "Unobserved task exception")]
    private static partial void LogUnobservedTaskException(ILogger logger, Exception exception);
}
