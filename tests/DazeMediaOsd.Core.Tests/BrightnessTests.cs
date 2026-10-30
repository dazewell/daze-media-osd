using System.Collections.Concurrent;
using DazeMediaOsd.Core.Brightness;
using DazeMediaOsd.Core.Osd;

namespace DazeMediaOsd.Core.Tests;

public class BrightnessOsdStateTests
{
    [Fact]
    public void TheLogLineNamesTheScreenAndItsLevel()
    {
        var state = new OsdState(null, null, null, OsdReason.Brightness, Brightness: new BrightnessState("Built-in screen (X)", 55));

        Assert.Equal("Brightness: out=none in=none brightness=\"Built-in screen (X)\" 55", state.Describe());
    }
}

public class BrightnessPlannerTests
{
    private static readonly int[] Full = [.. Enumerable.Range(0, 101)];
    private static readonly int[] Quarters = [0, 25, 50, 75, 100];

    [Theory]
    [InlineData(50, 5, 55)]
    [InlineData(50, -5, 45)]
    [InlineData(98, 5, 100)]
    [InlineData(100, 5, 100)]
    [InlineData(10, -5, 5)]
    [InlineData(7, -5, 5)]
    [InlineData(5, -5, 5)]
    [InlineData(3, -5, 3)] // a step down never raises it, not even from below the floor
    [InlineData(0, -5, 0)]
    [InlineData(3, 5, 8)]
    [InlineData(0, 2, 5)] // a step up from below the floor lands on it at the least
    [InlineData(50, 0, 50)]
    [InlineData(50, int.MaxValue, 100)]
    [InlineData(50, int.MinValue, 5)]
    public void EveryPercentIsOffered(int current, int delta, int expected) =>
        Assert.Equal(expected, BrightnessPlanner.Plan(current, Full, delta));

    [Theory]
    [InlineData(50, 5, 75)] // every step still moves one level
    [InlineData(50, -5, 25)]
    [InlineData(25, -5, 25)] // 0 is below the floor, so there is nowhere lower
    [InlineData(75, 30, 100)]
    [InlineData(0, 5, 25)]
    public void OnlyTheLevelsTheScreenListsAreChosen(int current, int delta, int expected) =>
        Assert.Equal(expected, BrightnessPlanner.Plan(current, Quarters, delta));

    [Fact]
    public void TheOrderOfTheLevelsDoesNotMatter() =>
        Assert.Equal(75, BrightnessPlanner.Plan(50, [100, 0, 75, 25, 50], 5));

    [Fact]
    public void LevelsOutsideZeroToHundredAreNeverChosen()
    {
        int[] levels = [-5, 50, 101, 200, int.MaxValue];

        Assert.Equal(50, BrightnessPlanner.Plan(30, levels, 5));
        Assert.Equal(30, BrightnessPlanner.Plan(30, levels, -5));
        Assert.Equal(50, BrightnessPlanner.Plan(60, levels, -5));
        Assert.Equal(60, BrightnessPlanner.Plan(60, levels, 5));
    }

    [Fact]
    public void NoLevelsMeansNoMove() =>
        Assert.Equal(50, BrightnessPlanner.Plan(50, [], 5));

    [Fact]
    public void TheFloorCanBeRaised() =>
        Assert.Equal(20, BrightnessPlanner.Plan(50, Full, -100, floor: 20));

    [Fact]
    public void BetweenTwoEquallyNearLevelsTheSmallerMoveWins()
    {
        Assert.Equal(20, BrightnessPlanner.Plan(10, [20, 30], 15));
        Assert.Equal(20, BrightnessPlanner.Plan(10, [30, 20], 15));
        Assert.Equal(30, BrightnessPlanner.Plan(40, [20, 30], -15));
    }

    [Fact]
    public void NoStepGoesTheWrongWayOrOffTheScaleOrBelowTheFloor()
    {
        int[][] screens = [Full, Quarters, [3, 40, 97], [0, 100], [100, 90, 80], [0, 1, 2]];
        foreach (var levels in screens)
        {
            for (var current = 0; current <= 100; current++)
            {
                for (var delta = -120; delta <= 120; delta++)
                {
                    var target = BrightnessPlanner.Plan(current, levels, delta);
                    var where = $"levels {levels.Length}, current {current}, delta {delta}, target {target}";
                    Assert.True(delta <= 0 || target >= current, where);
                    Assert.True(delta >= 0 || target <= current, where);
                    Assert.True(target == current || (levels.Contains(target) && target >= BrightnessPlanner.Floor), where);
                }
            }
        }
    }
}

public sealed class BrightnessServiceTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private readonly FakeBrightnessBackend _backend = new();
    private readonly BlockingCollection<BrightnessState> _answers = [];
    private BrightnessService? _service;

    public void Dispose()
    {
        _service?.Dispose();
        _answers.Dispose();
    }

    private BrightnessService Start(TimeSpan? settle = null)
    {
        _service = new BrightnessService(_backend, settle);
        _service.Changed += (_, state) => _answers.Add(state);
        return _service;
    }

    private BrightnessState Next() =>
        _answers.TryTake(out var state, (int)Patience.TotalMilliseconds, TestContext.Current.CancellationToken)
            ? state
            : throw new TimeoutException("the service never answered");

    // The worker runs everything queued before it stops, so after this nothing is in flight.
    private void Finish() => _service!.Dispose();

    // Holds the first write inside the backend until Release.
    private WriteHold HoldFirstWrite()
    {
        var hold = new WriteHold();
        _backend.InsideWrite = hold.Enter;
        return hold;
    }

    private sealed class WriteHold : IDisposable
    {
        private readonly ManualResetEventSlim _inside = new();
        private readonly ManualResetEventSlim _go = new();
        private int _writes;

        public void Enter()
        {
            if (Interlocked.Increment(ref _writes) != 1)
                return;
            _inside.Set();
            _go.Wait(Patience);
        }

        public bool WaitInside() => _inside.Wait(Patience, TestContext.Current.CancellationToken);

        public void Release() => _go.Set();

        public void Dispose()
        {
            _inside.Dispose();
            _go.Dispose();
        }
    }

    [Fact]
    public void NothingTouchesTheScreenUntilAsked()
    {
        Start();

        Finish();

        Assert.Equal(0, _backend.Reads);
        Assert.Empty(_backend.Writes);
        Assert.False(_service!.Probed);
    }

    [Fact]
    public void AStepReadsTheScreenThenWritesTheNextLevel()
    {
        var service = Start();

        service.Adjust(5);

        var answer = Next();
        Assert.Equal((FakeBrightnessBackend.PanelName, 55), (answer.Name, answer.Percent));
        Finish();
        Assert.Equal([(FakeBrightnessBackend.PanelId, 55)], _backend.Writes);
    }

    [Fact]
    public void EveryStepStartsFromWhatTheScreenShowsNow()
    {
        var service = Start();
        service.Adjust(5);
        Assert.Equal(55, Next().Percent);

        _backend.Screen = FakeBrightnessBackend.Panel(80); // another program changed it meanwhile
        service.Adjust(5);

        Assert.Equal(85, Next().Percent);
        Finish();
        Assert.Equal([55, 85], _backend.WrittenLevels);
    }

    [Fact]
    public void StepsThatPileUpWhileOneIsAppliedAreAppliedAsOne()
    {
        using var hold = HoldFirstWrite();
        var service = Start();

        service.Adjust(5);
        Assert.True(hold.WaitInside());
        service.Adjust(5);
        service.Adjust(5);
        service.Adjust(5);
        hold.Release();

        Assert.Equal(55, Next().Percent);
        Assert.Equal(70, Next().Percent);
        Finish();
        Assert.Equal([55, 70], _backend.WrittenLevels);
    }

    [Fact]
    public void StepsThatCancelOutWriteNothing()
    {
        using var hold = HoldFirstWrite();
        var service = Start();

        service.Adjust(5);
        Assert.True(hold.WaitInside());
        service.Adjust(5);
        service.Adjust(-5);
        hold.Release();

        Assert.Equal(55, Next().Percent);
        Finish();
        Assert.Equal([55], _backend.WrittenLevels);
    }

    [Fact]
    public void TheWheelNeverTakesTheScreenBelowTheFloor()
    {
        _backend.Screen = FakeBrightnessBackend.Panel(7);
        var service = Start();

        service.Adjust(-5);
        Assert.Equal(BrightnessPlanner.Floor, Next().Percent);
        service.Adjust(-5);
        Assert.Equal(BrightnessPlanner.Floor, Next().Percent); // at the limit: nothing written, the OSD still answers

        Finish();
        Assert.Equal([BrightnessPlanner.Floor], _backend.WrittenLevels);
    }

    [Fact]
    public void AScreenBelowTheFloorIsNeverRaisedByAStepDown()
    {
        _backend.Screen = FakeBrightnessBackend.Panel(3);
        var service = Start();

        service.Adjust(-5);

        Assert.Equal(3, Next().Percent);
        Finish();
        Assert.Empty(_backend.Writes);
    }

    [Fact]
    public void AtTheTopNothingIsWrittenButTheOsdStillAnswers()
    {
        _backend.Screen = FakeBrightnessBackend.Panel(100);
        var service = Start();

        service.Adjust(5);

        Assert.Equal(100, Next().Percent);
        Finish();
        Assert.Empty(_backend.Writes);
    }

    [Fact]
    public void OnlyLevelsTheScreenListsAreWritten()
    {
        _backend.Screen = FakeBrightnessBackend.Panel(50, [0, 25, 50, 75, 100]);
        var service = Start();

        service.Adjust(5);

        Assert.Equal(75, Next().Percent);
        Finish();
        Assert.Equal([75], _backend.WrittenLevels);
    }

    [Fact]
    public void WithoutAScreenNothingIsWrittenOrShown()
    {
        _backend.Screen = null;
        var service = Start();

        service.Adjust(5);
        Finish();

        Assert.Empty(_backend.Writes);
        Assert.Empty(_answers);
        Assert.Null(service.ScreenName);
        Assert.True(service.Probed);
    }

    [Fact]
    public void AFailedReadWritesNothingAndStartsOver()
    {
        _backend.ReadError = new InvalidOperationException("WMI is down");
        var service = Start();

        service.Adjust(5);
        Finish();

        Assert.Empty(_backend.Writes);
        Assert.Equal(1, _backend.Resets);
        Assert.Empty(_answers);
    }

    [Fact]
    public void AFailedWriteDropsWhatPiledUpMeanwhileAndStartsOver()
    {
        using var hold = HoldFirstWrite();
        _backend.WriteError = new InvalidOperationException("access denied");
        var service = Start();

        service.Adjust(5);
        Assert.True(hold.WaitInside());
        service.Adjust(5);
        service.Adjust(5);
        hold.Release();
        Finish();

        Assert.Single(_backend.Writes); // the one that failed; the pile is not applied late
        Assert.Equal(1, _backend.Resets);
        Assert.Empty(_answers);
    }

    [Fact]
    public void AfterAFailureTheNextStepStartsOver()
    {
        _backend.ReadError = new InvalidOperationException("WMI is down");
        var service = Start();
        service.Adjust(5);
        Assert.True(SpinWait.SpinUntil(() => _backend.Resets == 1, Patience));
        _backend.ReadError = null;

        service.Adjust(5);

        Assert.Equal(55, Next().Percent);
    }

    [Theory]
    [InlineData(150, "panel-1")]
    [InlineData(-1, "panel-1")]
    [InlineData(50, "")]
    public void AnImpossibleReadingIsAFailureNotAPlan(int level, string id)
    {
        _backend.Screen = new BrightnessPanel(id, FakeBrightnessBackend.PanelName, level, [0, 50, 100]);
        var service = Start();

        service.Adjust(5);
        Finish();

        Assert.Empty(_backend.Writes);
        Assert.Equal(1, _backend.Resets);
        Assert.Empty(_answers);
    }

    [Fact]
    public void AReadingWithoutLevelsIsAFailureNotAPlan()
    {
        _backend.Screen = new BrightnessPanel(FakeBrightnessBackend.PanelId, FakeBrightnessBackend.PanelName, 50, null!);
        var service = Start();

        service.Adjust(5);
        Finish();

        Assert.Empty(_backend.Writes);
        Assert.Equal(1, _backend.Resets);
    }

    [Fact]
    public void ASlowReadCatchesUpBeforeTheOsdAnswers()
    {
        _backend.StaleReads = 3;
        var service = Start(settle: TimeSpan.FromSeconds(2));

        service.Adjust(5);

        Assert.Equal(55, Next().Percent);
    }

    [Fact]
    public void TheOsdShowsWhatTheScreenReportsNotWhatWasAskedFor()
    {
        _backend.IgnoreWrites = true;
        var service = Start(settle: TimeSpan.FromMilliseconds(40));

        service.Adjust(5);

        Assert.Equal(50, Next().Percent);
    }

    [Fact]
    public void WhenTheReadBackFailsTheLevelAskedForIsShown()
    {
        _backend.FailReadsAfterWrite = true;
        var service = Start();

        service.Adjust(5);

        Assert.Equal(55, Next().Percent);
        Finish();
        Assert.Equal(1, _backend.Resets);
    }

    [Fact]
    public void AReadBackThatKeepsFailingIsOneOutageUntilAStepGetsThrough()
    {
        _backend.FailReadBacks = true;
        var service = Start();

        service.Adjust(5);
        Assert.Equal(55, Next().Percent);
        service.Adjust(5);
        Assert.Equal(60, Next().Percent);
        service.Adjust(5);
        Assert.Equal(65, Next().Percent);
        Assert.Equal(3, _backend.Resets);
        Assert.Equal(1, service.Outages); // the write worked each time, yet nothing got through its read-back

        _backend.FailReadBacks = false;
        service.Adjust(5);
        Assert.Equal(70, Next().Percent);
        _backend.FailReadBacks = true;
        service.Adjust(5);
        Assert.Equal(75, Next().Percent);
        Finish();

        Assert.Equal(2, service.Outages);
    }

    [Fact]
    public void ZeroPointsAreNotAStep()
    {
        var service = Start();

        service.Adjust(0);
        Finish();

        Assert.Equal(0, _backend.Reads);
    }

    [Fact]
    public void RefreshFindsTheScreenAndNeverWrites()
    {
        using var changed = new SemaphoreSlim(0);
        var service = Start();
        service.ScreenChanged += (_, _) => changed.Release();

        service.Refresh();
        Assert.True(changed.Wait(Patience, TestContext.Current.CancellationToken));
        service.Refresh(); // nothing new: no second event
        Finish();

        Assert.Equal(FakeBrightnessBackend.PanelName, service.ScreenName);
        Assert.True(service.Probed);
        Assert.Equal(0, changed.CurrentCount);
        Assert.Empty(_backend.Writes);
    }

    [Fact]
    public void RefreshSeesAScreenThatWent()
    {
        using var changed = new SemaphoreSlim(0);
        var service = Start();
        service.ScreenChanged += (_, _) => changed.Release();
        service.Refresh();
        Assert.True(changed.Wait(Patience, TestContext.Current.CancellationToken));

        _backend.Screen = null; // the lid closed, or the dock took it
        service.Refresh();

        Assert.True(changed.Wait(Patience, TestContext.Current.CancellationToken));
        Assert.Null(service.ScreenName);
        Assert.True(service.Probed);
    }

    [Fact]
    public void AFailedRefreshReportsNoScreenAndNeverWrites()
    {
        using var changed = new SemaphoreSlim(0);
        _backend.ReadError = new InvalidOperationException("WMI is down");
        var service = Start();
        service.ScreenChanged += (_, _) => changed.Release();

        service.Refresh();

        Assert.True(changed.Wait(Patience, TestContext.Current.CancellationToken));
        Assert.Null(service.ScreenName);
        Finish();
        Assert.Equal(1, _backend.Resets);
        Assert.Empty(_backend.Writes);
    }

    [Fact]
    public void AHandlerThatThrowsDoesNotStopTheNextStep()
    {
        var service = Start();
        var first = true;
        service.Changed += (_, _) =>
        {
            if (!first)
                return;
            first = false;
            throw new InvalidOperationException("handler bug");
        };

        service.Adjust(5);
        Assert.Equal(55, Next().Percent);
        service.Adjust(5);

        Assert.Equal(60, Next().Percent);
    }

    [Fact]
    public void DisposeStopsTheScreenWorkAndLateCallsAreIgnored()
    {
        var service = Start();
        service.Dispose();

        service.Adjust(5);
        service.Refresh();
        service.Dispose();

        Assert.True(_backend.Disposed);
        Assert.Equal(0, _backend.Reads);
        Assert.Empty(_backend.Writes);
    }

    [Fact]
    public void StepsRacingDisposeNeverReachAClosedScreen()
    {
        for (var round = 0; round < 200; round++)
        {
            var backend = new FakeBrightnessBackend();
            var service = new BrightnessService(backend);
            var stop = 0;
            var wheel = new Thread(() =>
            {
                while (Volatile.Read(ref stop) == 0)
                    service.Adjust(1);
            }) { IsBackground = true };
            wheel.Start();
            Assert.True(SpinWait.SpinUntil(() => backend.Reads > 0, Patience));

            service.Dispose();
            Volatile.Write(ref stop, 1);
            Assert.True(wheel.Join(Patience));

            Assert.True(backend.Disposed);
            Assert.False(backend.UsedAfterDispose, $"round {round}");
        }
    }
}
