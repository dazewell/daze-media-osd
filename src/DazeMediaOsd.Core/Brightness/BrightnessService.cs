using System.Collections.Concurrent;
using System.Diagnostics;
using DazeMediaOsd.Core.Diagnostics;

namespace DazeMediaOsd.Core.Brightness;

/// <summary>
/// Steps the built-in screen's brightness, built so it cannot do harm (D-041). Everything that touches the screen
/// runs on one MTA worker, one operation at a time; the wheel thread only adds up what was asked, so a fast
/// scroll becomes few writes however slow the screen is. Every step reads the screen first and writes only a level
/// it lists, never below <see cref="BrightnessPlanner.Floor"/>. A failed read or write is dropped with what had
/// piled up meanwhile, never retried; the next step starts a fresh connection. Nothing runs until someone asks,
/// a step or <see cref="Refresh"/>, and all that happens besides a step is a read.
/// </summary>
public sealed class BrightnessService : IBrightnessService
{
    // A burst of steps logs once: the line says where it started.
    private static readonly TimeSpan LogGap = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan SettleTimeout = TimeSpan.FromMilliseconds(300);
    private const int SettlePoll = 5; // ms; measured on a real panel, a read catches up within ~20 ms

    private readonly IBrightnessBackend _backend;
    private readonly TimeSpan _settleTimeout;
    private readonly BlockingCollection<Action> _work = [];
    private readonly Thread _worker;
    private int _pending; // points asked for and not applied yet
    private int _drainQueued; // 1 while a Drain waits in the queue
    private int _disposed;
    private volatile string? _screenName;
    private volatile bool _probed;
    // Worker only:
    private bool _failing;
    private long _lastLogged;
    private bool _logged;

    public BrightnessService(IBrightnessBackend backend, TimeSpan? settleTimeout = null)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _settleTimeout = settleTimeout ?? SettleTimeout;
        _worker = new Thread(RunWorker) { IsBackground = true, Name = "Brightness" };
        _worker.SetApartmentState(ApartmentState.MTA);
        _worker.Start();
    }

    public string? ScreenName => _screenName;

    public bool Probed => _probed;

    public event EventHandler? ScreenChanged;

    public event EventHandler<BrightnessState>? Changed;

    public void Refresh() => Post(Probe);

    public void Adjust(int percent)
    {
        if (percent == 0)
            return;
        Interlocked.Add(ref _pending, percent);
        if (Interlocked.Exchange(ref _drainQueued, 1) == 0)
            Post(Drain);
    }

    // Steps that arrive while this runs queue another Drain, which finds them in _pending.
    private void Drain()
    {
        Interlocked.Exchange(ref _drainQueued, 0);
        var delta = Interlocked.Exchange(ref _pending, 0);
        if (delta == 0)
            return;
        BrightnessState? state;
        try
        {
            state = Apply(delta);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            FailStep("setting the brightness", e);
            return;
        }
        if (state is null)
            return;
        try
        {
            Changed?.Invoke(this, state);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Log.Error("brightness handler", e);
        }
    }

    // Null when there is nothing to show: no screen.
    private BrightnessState? Apply(int delta)
    {
        if (ReadPanel() is not { } panel)
        {
            Recovered();
            return null;
        }
        var target = BrightnessPlanner.Plan(panel.Level, panel.Levels, delta);
        if (target == panel.Level)
        {
            Recovered();
            return new BrightnessState(panel.Name, panel.Level);
        }
        LogStep(panel, target);
        _backend.Write(panel.Id, target);
        Recovered();
        return new BrightnessState(panel.Name, ReadBack(panel, target));
    }

    // A read just after a write can still show the old level for a few milliseconds (D-041), so wait until it moves.
    // The OSD says what the screen reports, not what was asked for. If that can't be read the write still happened,
    // so the level asked for is the best there is.
    private int ReadBack(BrightnessPanel before, int asked)
    {
        var start = Stopwatch.GetTimestamp();
        try
        {
            while (_backend.Read() is { } after && after.Id == before.Id && after.Level is >= 0 and <= 100)
            {
                if (after.Level == before.Level && Stopwatch.GetElapsedTime(start) < _settleTimeout)
                {
                    Thread.Sleep(SettlePoll);
                    continue;
                }
                if (after.Level != asked)
                    Log.Info($"brightness: asked for {asked}, the screen reports {after.Level}");
                return after.Level;
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            FailStep("reading the brightness back", e);
        }
        return asked;
    }

    // The one place a screen is read before a write: anything it says that can't be true is a failure, so no write follows.
    private BrightnessPanel? ReadPanel()
    {
        var panel = _backend.Read();
        if (panel is not null && (panel.Level is < 0 or > 100 || string.IsNullOrEmpty(panel.Id) || panel.Levels is null))
            throw new InvalidDataException($"the screen reports level {panel.Level}, which it can't have");
        SetScreen(panel?.Name);
        return panel;
    }

    private void Probe()
    {
        try
        {
            ReadPanel();
            Recovered();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            SetScreen(null);
            Fail("looking for the built-in screen", e);
        }
    }

    private void SetScreen(string? name)
    {
        var changed = !_probed || _screenName != name;
        _screenName = name;
        _probed = true;
        if (!changed)
            return;
        Log.Info(name is null ? "brightness: no built-in screen found" : $"brightness: built-in screen \"{name}\"");
        try
        {
            ScreenChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Log.Error("brightness screen handler", e);
        }
    }

    private void LogStep(BrightnessPanel panel, int target)
    {
        if (_logged && Stopwatch.GetElapsedTime(_lastLogged) < LogGap)
        {
            _lastLogged = Stopwatch.GetTimestamp();
            return;
        }
        (_logged, _lastLogged) = (true, Stopwatch.GetTimestamp());
        Log.Info($"brightness: {panel.Level} -> {target}, {panel.Name}");
    }

    // Whatever piled up while a step failed is as stale as the step: it is dropped, not applied late.
    private void FailStep(string what, Exception e)
    {
        Interlocked.Exchange(ref _pending, 0);
        Fail(what, e);
    }

    // A connection that failed once is dropped, not nursed: the next step builds a new one. One log line per
    // outage, so a broken WMI can't fill the log with one error per wheel notch.
    private void Fail(string what, Exception e)
    {
        try
        {
            _backend.Reset();
        }
        catch (Exception reset) when (reset is not OutOfMemoryException)
        {
            Log.Error("brightness: resetting after a failure", reset);
        }
        if (_failing)
            return;
        _failing = true;
        Log.Error($"brightness: {what} failed; the next failures are not logged until it works again", e);
    }

    private void Recovered()
    {
        if (!_failing)
            return;
        _failing = false;
        Log.Info("brightness: works again");
    }

    // Late posts after Dispose are dropped (false).
    private bool Post(Action action)
    {
        try
        {
            _work.Add(action);
            return true;
        }
        catch (Exception e) when (e is InvalidOperationException or ObjectDisposedException)
        {
            return false;
        }
    }

    private void RunWorker()
    {
        foreach (var action in _work.GetConsumingEnumerable())
        {
            try
            {
                action();
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                Log.Error("brightness", e);
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        Post(() => _backend.Dispose());
        _work.CompleteAdding();
        // A WMI call that never returns must not hold the app's exit: the thread is a background one.
        if (!_worker.Join(StopTimeout))
        {
            Log.Info("brightness: the worker didn't stop in time; leaving it");
            return;
        }
        _work.Dispose();
    }
}
