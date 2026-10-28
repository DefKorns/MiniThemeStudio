using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MiniThemeStudio.App.Diagnostics;
using MiniThemeStudio.App.Dialogs;

namespace MiniThemeStudio.App.Errors;

public sealed partial class ErrorHandler(ILogger<ErrorHandler> logger, IDialogService dialogService) : IErrorHandler
{
    private bool _isShowingError;

    public async Task HandleAsync(Exception exception, string failure)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentException.ThrowIfNullOrWhiteSpace(failure);

        string message;
        if (IsExpected(exception))
        {
            LogExpectedFailure(logger, exception, failure);
            message = exception.Message;
        }
        else
        {
            LogUnexpectedFailure(logger, exception, failure);
            message = $"{exception.Message}\n\nThis was not expected. The details were written to the log in {FileLogging.LogsDirectory}.";
        }

        // one error dialog at a time, so an error raised while showing one cannot loop
        if (_isShowingError)
        {
            return;
        }

        _isShowingError = true;
        try
        {
            await dialogService.ShowInformationAsync(failure, message, DialogVariant.Error);
        }
        catch (Exception dialogException) when (dialogException is InvalidOperationException)
        {
            LogDialogFailure(logger, dialogException);
        }
        finally
        {
            _isShowingError = false;
        }
    }

    public async Task<bool> TryRunAsync(string failure, Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        try
        {
            await action();
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            await HandleAsync(exception, failure);
            return false;
        }
    }

    // failures caused by the files or the user's input, not by a bug
    private static bool IsExpected(Exception exception) => exception
        is OperationFailedException
        or IOException
        or UnauthorizedAccessException
        or InvalidDataException
        or JsonException
        or ArgumentException;

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Failure}")]
    private static partial void LogExpectedFailure(ILogger logger, Exception exception, string failure);

    [LoggerMessage(Level = LogLevel.Error, Message = "{Failure} (unexpected)")]
    private static partial void LogUnexpectedFailure(ILogger logger, Exception exception, string failure);

    [LoggerMessage(Level = LogLevel.Error, Message = "The error dialog could not be shown")]
    private static partial void LogDialogFailure(ILogger logger, Exception exception);
}
