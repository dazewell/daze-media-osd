using DazeMediaOsd.Core.Lifecycle;

namespace DazeMediaOsd.Core.Tests;

public sealed class ExitSignalTests
{
    private readonly string _name = @"Local\DazeMediaOsd.Tests.Exit." + Guid.NewGuid().ToString("N");

    [Fact]
    public void RequestWithoutListenerReportsNothingRunning() => Assert.False(ExitSignal.Request(_name));

    [Fact]
    public void RequestReachesTheListenerOnce()
    {
        var calls = 0;
        using var fired = new ManualResetEventSlim();
        using var signal = ExitSignal.Listen(() =>
        {
            Interlocked.Increment(ref calls);
            fired.Set();
        }, _name);

        Assert.True(ExitSignal.Request(_name));
        Assert.True(fired.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        ExitSignal.Request(_name);
        Thread.Sleep(200);

        Assert.Equal(1, calls);
    }

    [Fact]
    public void DisposedListenerNoLongerCounts()
    {
        ExitSignal.Listen(() => { }, _name).Dispose();

        Assert.False(ExitSignal.Request(_name));
    }
}
