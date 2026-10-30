using DazeMediaOsd.Core.Audio;
using DazeMediaOsd.Core.Brightness;
using DazeMediaOsd.Core.Input;

namespace DazeMediaOsd.Core.Tests;

/// <summary>Timestamps and one-shot timers that only move on <see cref="Advance"/>.</summary>
internal sealed class ManualTime : TimeProvider
{
    private readonly List<ManualTimer> _timers = [];
    private long _ticks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => _ticks;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        _timers.Add(timer);
        return timer;
    }

    public void Advance(TimeSpan by)
    {
        _ticks += by.Ticks;
        foreach (var timer in _timers.ToList())
            timer.FireIfDue(_ticks);
    }

    /// <summary>Runs every timer's callback, due or not: one the pool queued before a Change/Dispose.</summary>
    public void RunAllCallbacks()
    {
        foreach (var timer in _timers.ToList())
            timer.Run();
    }

    internal sealed class ManualTimer(ManualTime time, TimerCallback callback, object? state) : ITimer
    {
        public void Run() => callback(state);

        private long? _dueAt;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            _dueAt = dueTime == Timeout.InfiniteTimeSpan ? null : time._ticks + dueTime.Ticks;
            return true;
        }

        public void FireIfDue(long now)
        {
            if (_dueAt is not { } due || due > now)
                return;
            _dueAt = null; // one-shot is all the controller uses
            callback(state);
        }

        public void Dispose() => _dueAt = null;

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

internal sealed class FakeAudio : IAudioService
{
    public AudioSnapshot Current { get; set; } = AudioSnapshot.Empty;

    public event EventHandler<AudioChangedEventArgs>? Changed;

    public IReadOnlyList<AudioEndpoint> Endpoints(EndpointFlow flow) => [];

    /// <summary>Every <see cref="SetDefault"/> call, in order.</summary>
    public List<(string Id, EndpointFlow Flow)> Defaults { get; } = [];

    /// <summary>What <see cref="Connect"/> answers; every call is recorded in <see cref="Connects"/>.</summary>
    public ConnectResult ConnectAnswer { get; set; } = ConnectResult.Requested;

    public List<string> Connects { get; } = [];

    /// <summary>Runs inside <see cref="Connect"/>, before it answers: a device that comes up meanwhile.</summary>
    public Action<string>? WhileConnecting { get; set; }

    public void SetDefault(string id, EndpointFlow flow) => Defaults.Add((id, flow));

    /// <summary>Every <see cref="StepOutputVolume"/> delta, in order.</summary>
    public List<float> VolumeSteps { get; } = [];

    public void StepOutputVolume(float delta) => VolumeSteps.Add(delta);

    public ConnectResult Connect(string id)
    {
        Connects.Add(id);
        WhileConnecting?.Invoke(id);
        return ConnectAnswer;
    }

    public void Change(AudioSnapshot next, EndpointFlow flow, AudioChangeKind kind, DeviceEvent? device = null)
    {
        var previous = Current;
        Current = next;
        Changed?.Invoke(this, new AudioChangedEventArgs(previous, next, flow, kind, device));
    }

    public void Device(DeviceEvent device) =>
        Change(Current, device.Flow, device.Connected ? AudioChangeKind.DeviceConnected : AudioChangeKind.DeviceDisconnected, device);

    public void Dispose()
    {
    }
}

internal sealed class FakeWheel : ITaskbarWheelSource
{
    public event EventHandler<TaskbarWheelEventArgs>? Wheeled;

    public void Turn(TaskbarZone zone, int delta, HeldModifiers held = HeldModifiers.None) =>
        Wheeled?.Invoke(this, new TaskbarWheelEventArgs(zone, delta, held));

    public void Dispose()
    {
    }
}

/// <summary>For the wheel: only records what it was asked for.</summary>
internal sealed class FakeBrightness : IBrightnessService
{
    public string? ScreenName => null;

    public bool Probed => false;

    public event EventHandler? ScreenChanged { add { } remove { } }

    public event EventHandler<BrightnessState>? Changed { add { } remove { } }

    /// <summary>Every <see cref="Adjust"/> amount, in order.</summary>
    public List<int> Steps { get; } = [];

    public void Refresh()
    {
    }

    public void Adjust(int percent) => Steps.Add(percent);

    public void Dispose()
    {
    }
}

/// <summary>
/// A screen that can lag behind a write, ignore it, fail, or hold the worker inside a write. The service calls it
/// from its own thread while a test reads it, so everything goes through one lock.
/// </summary>
internal sealed class FakeBrightnessBackend : IBrightnessBackend
{
    public const string PanelId = "panel-1";
    public const string PanelName = "Built-in screen";

    private readonly Lock _lock = new();
    private readonly List<(string Id, int Level)> _writes = [];
    private BrightnessPanel? _screen = Panel(50);
    private Exception? _readError;
    private Exception? _writeError;
    private bool _ignoreWrites;
    private bool _failReadsAfterWrite;
    private int _staleReads;
    private int _staleReadsLeft;
    private int _staleLevel;
    private int _reads;
    private int _resets;
    private bool _disposed;

    /// <summary>A screen that takes every percent unless <paramref name="levels"/> says otherwise.</summary>
    public static BrightnessPanel Panel(int level, IReadOnlyList<int>? levels = null) =>
        new(PanelId, PanelName, level, levels ?? [.. Enumerable.Range(0, 101)]);

    /// <summary>The screen as it is; null is a PC without one. Setting it is another program changing the brightness.</summary>
    public BrightnessPanel? Screen
    {
        get { lock (_lock) return _screen; }
        set { lock (_lock) _screen = value; }
    }

    public Exception? ReadError
    {
        get { lock (_lock) return _readError; }
        set { lock (_lock) _readError = value; }
    }

    public Exception? WriteError
    {
        get { lock (_lock) return _writeError; }
        set { lock (_lock) _writeError = value; }
    }

    /// <summary>The write is accepted and nothing happens, as on a screen that is busy.</summary>
    public bool IgnoreWrites
    {
        get { lock (_lock) return _ignoreWrites; }
        set { lock (_lock) _ignoreWrites = value; }
    }

    /// <summary>Every read fails once something has been written.</summary>
    public bool FailReadsAfterWrite
    {
        get { lock (_lock) return _failReadsAfterWrite; }
        set { lock (_lock) _failReadsAfterWrite = value; }
    }

    /// <summary>After a write, this many reads still show the old level (D-041).</summary>
    public int StaleReads
    {
        get { lock (_lock) return _staleReads; }
        set { lock (_lock) _staleReads = value; }
    }

    /// <summary>Runs inside <see cref="Write"/>, before it does anything: a test can hold the worker there.</summary>
    public Action? InsideWrite { get; set; }

    /// <summary>Every <see cref="Write"/> call, failed or not.</summary>
    public IReadOnlyList<(string Id, int Level)> Writes
    {
        get { lock (_lock) return [.. _writes]; }
    }

    public int[] WrittenLevels
    {
        get { lock (_lock) return [.. _writes.Select(w => w.Level)]; }
    }

    public int Reads
    {
        get { lock (_lock) return _reads; }
    }

    public int Resets
    {
        get { lock (_lock) return _resets; }
    }

    public bool Disposed
    {
        get { lock (_lock) return _disposed; }
    }

    public BrightnessPanel? Read()
    {
        lock (_lock)
        {
            _reads++;
            if (_readError is { } error)
                throw error;
            if (_screen is not null && _staleReadsLeft > 0)
            {
                _staleReadsLeft--;
                return _screen with { Level = _staleLevel };
            }
            return _screen;
        }
    }

    public void Write(string id, int level)
    {
        InsideWrite?.Invoke();
        lock (_lock)
        {
            _writes.Add((id, level));
            if (_writeError is { } error)
                throw error;
            if (_ignoreWrites || _screen is null)
                return;
            _staleLevel = _screen.Level;
            _staleReadsLeft = _staleReads;
            _screen = _screen with { Level = level };
            if (_failReadsAfterWrite)
                _readError = new IOException("the screen stopped answering");
        }
    }

    public void Reset()
    {
        lock (_lock)
            _resets++;
    }

    public void Dispose()
    {
        lock (_lock)
            _disposed = true;
    }
}
