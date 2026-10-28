using System;
using System.Threading.Tasks;

namespace MiniThemeStudio.App.Errors;

// The one place that reports failures: logs them and tells the user.
public interface IErrorHandler
{
    // failure is what could not be done, e.g. "Cannot open theme"
    Task HandleAsync(Exception exception, string failure);

    // runs an action and reports what it throws; returns false when it failed
    Task<bool> TryRunAsync(string failure, Func<Task> action);
}
