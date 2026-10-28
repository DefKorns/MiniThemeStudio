using MiniThemeStudio.App.Services;

namespace MiniThemeStudio.App.Tests.Services;

public sealed class SingleInstanceTests
{
    // a name of its own, so the tests never meet a running Mini Theme Studio
    private readonly string _name = $"MiniThemeStudio.Tests.{Guid.NewGuid():N}";

    [Fact]
    public void OnlyTheFirstStartOwnsTheInstance()
    {
        using var first = SingleInstance.Acquire(_name);
        using var second = SingleInstance.Acquire(_name);

        Assert.True(first.IsFirst);
        Assert.False(second.IsFirst);
    }

    [Fact]
    public void TheInstanceIsFreeAgainOnceTheFirstOneCloses()
    {
        SingleInstance.Acquire(_name).Dispose();

        using var next = SingleInstance.Acquire(_name);

        Assert.True(next.IsFirst);
    }

    [Fact]
    public void ALaterStartBringsTheFirstOneForward()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var first = SingleInstance.Acquire(_name);
        using var activated = new ManualResetEventSlim();
        first.ListenForActivation(activated.Set);
        using var second = SingleInstance.Acquire(_name);

        second.ActivateFirst();

        Assert.True(activated.Wait(TimeSpan.FromSeconds(5)));
    }
}
