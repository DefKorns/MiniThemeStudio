using MiniThemeStudio.App.Errors;

namespace MiniThemeStudio.App.Tests.TestDoubles;

// handles failures like the real handler but keeps them for the test instead of logging and showing them
public sealed class RecordingErrorHandler : IErrorHandler
{
    public List<(Exception Exception, string Failure)> Failures { get; } = [];

    public Task HandleAsync(Exception exception, string failure)
    {
        Failures.Add((exception, failure));
        return Task.CompletedTask;
    }

    public async Task<bool> TryRunAsync(string failure, Func<Task> action)
    {
        try
        {
            await action();
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Failures.Add((exception, failure));
            return false;
        }
    }
}
