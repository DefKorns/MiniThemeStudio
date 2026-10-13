using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using MiniThemeStudio.Core.Firmware;

namespace MiniThemeStudio.App.Views;

public sealed partial class ConsoleConnectionDialog : Window, IDisposable
{
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly Func<CancellationToken, Task<InstalledBaseInfo>> _operation;

    public ConsoleConnectionDialog()
        : this(_ => Task.FromException<InstalledBaseInfo>(
            new InvalidOperationException("No console operation was configured.")))
    {
    }

    public ConsoleConnectionDialog(
        Func<CancellationToken, Task<InstalledBaseInfo>> operation)
    {
        _operation = operation;
        InitializeComponent();
        Opened += Dialog_Opened;
    }

    public InstalledBaseInfo? Result { get; private set; }

    public Exception? Error { get; private set; }

    private async void Dialog_Opened(object? sender, EventArgs e)
    {
        try
        {
            Result = await _operation(_cancellationTokenSource.Token);
            Close(true);
        }
        catch (OperationCanceledException)
        {
            Close(false);
        }
        catch (Exception exception)
        {
            Error = exception;
            Close(false);
        }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        _cancellationTokenSource.Cancel();
    }

    protected override void OnClosed(EventArgs e)
    {
        Opened -= Dialog_Opened;
        _cancellationTokenSource.Cancel();
        base.OnClosed(e);
    }

    public void Dispose()
    {
        _cancellationTokenSource.Dispose();
        GC.SuppressFinalize(this);
    }
}
