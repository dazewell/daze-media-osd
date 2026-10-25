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

    // Not "Request returns false": when Windows frees the event after Dispose varies (CI kept it openable).
    [Fact]
    public void DisposedListenerIsNotCalled()
    {
        var calls = 0;
        ExitSignal.Listen(() => Interlocked.Increment(ref calls), _name).Dispose();

        ExitSignal.Request(_name);
        Thread.Sleep(200);

        Assert.Equal(0, calls);
    }
}
