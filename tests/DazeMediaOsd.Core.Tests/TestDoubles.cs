using DazeMediaOsd.Core.Audio;

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
