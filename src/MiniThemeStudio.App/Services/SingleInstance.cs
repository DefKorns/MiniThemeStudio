using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace MiniThemeStudio.App.Services;

// One app per user session: a second start brings the running window to the front and exits, so the same
// drafts and settings are never edited from two windows.
public sealed partial class SingleInstance : IDisposable
{
    public const string DefaultName = "MiniThemeStudio.App";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle? _activation;
    private RegisteredWaitHandle? _listener;

    private SingleInstance(string name)
    {
        // Local\ keeps the instance per user session (other users on the machine run their own)
        _mutex = new Mutex(true, $@"Local\{name}", out var isFirst);
        IsFirst = isFirst;

        // named events exist on Windows only; elsewhere a second start just exits
        if (OperatingSystem.IsWindows())
        {
            _activation = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\{name}.Activate");
        }
    }

    public bool IsFirst { get; }

    public static SingleInstance Acquire(string name = DefaultName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new SingleInstance(name);
    }

    // in the first instance: called on a pool thread whenever another start asks to be shown instead
    public void ListenForActivation(Action activate)
    {
        ArgumentNullException.ThrowIfNull(activate);
        if (IsFirst && _activation is not null)
        {
            _listener = ThreadPool.RegisterWaitForSingleObject(_activation, (_, _) => activate(), null, Timeout.Infinite, executeOnlyOnce: false);
        }
    }

    // in a later instance: asks the first one to come to the front
    public void ActivateFirst()
    {
        if (IsFirst || _activation is null)
        {
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            // the process the user just started may hand its right to take the foreground to the running one
            AllowSetForegroundWindow(AnyProcess);
        }

        _activation.Set();
    }

    public void Dispose()
    {
        _listener?.Unregister(null);
        _activation?.Dispose();
        if (IsFirst)
        {
            _mutex.ReleaseMutex();
        }

        _mutex.Dispose();
    }

    private const int AnyProcess = -1;

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(int processId);
}
