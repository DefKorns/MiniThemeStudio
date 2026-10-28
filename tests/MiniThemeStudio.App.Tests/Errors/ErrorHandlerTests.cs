using Microsoft.Extensions.Logging.Abstractions;
using MiniThemeStudio.App.Errors;
using MiniThemeStudio.App.Tests.TestDoubles;

namespace MiniThemeStudio.App.Tests.Errors;

public sealed class ErrorHandlerTests
{
    private readonly FakeDialogService _dialogs = new();
    private readonly ErrorHandler _handler;

    public ErrorHandlerTests()
    {
        _handler = new ErrorHandler(NullLogger<ErrorHandler>.Instance, _dialogs);
    }

    [Fact]
    public async Task AnExpectedFailureShowsItsOwnMessage()
    {
        await _handler.HandleAsync(new IOException("The file is in use."), "Cannot save theme");

        var (title, message) = Assert.Single(_dialogs.Shown);
        Assert.Equal("Cannot save theme", title);
        Assert.Equal("The file is in use.", message);
    }

    [Fact]
    public async Task AnUnexpectedFailurePointsToTheLog()
    {
        await _handler.HandleAsync(new FormatException("Unexpected token."), "Cannot open theme");

        var (_, message) = Assert.Single(_dialogs.Shown);
        Assert.StartsWith("Unexpected token.", message);
        Assert.Contains("not expected", message);
        Assert.Contains("log", message);
    }

    [Fact]
    public async Task TryRunReportsTheFailureAndReturnsFalse()
    {
        var succeeded = await _handler.TryRunAsync("Cannot export package", () => throw new OperationFailedException("Fix 1 validation error(s)."));

        Assert.False(succeeded);
        Assert.Equal(("Cannot export package", "Fix 1 validation error(s)."), Assert.Single(_dialogs.Shown));
    }

    [Fact]
    public async Task TryRunReturnsTrueWhenNothingFails()
    {
        var ran = false;

        var succeeded = await _handler.TryRunAsync("Cannot do it", () =>
        {
            ran = true;
            return Task.CompletedTask;
        });

        Assert.True(succeeded);
        Assert.True(ran);
        Assert.Empty(_dialogs.Shown);
    }
}
