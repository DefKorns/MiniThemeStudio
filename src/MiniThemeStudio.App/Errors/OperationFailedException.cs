using System;

namespace MiniThemeStudio.App.Errors;

// An action that cannot go on for a reason the user can fix (validation errors, no Base chosen...);
// its message is shown as is.
public sealed class OperationFailedException : Exception
{
    public OperationFailedException()
    {
    }

    public OperationFailedException(string message)
        : base(message)
    {
    }

    public OperationFailedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
