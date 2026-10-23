using DazeMediaOsd.Core.Diagnostics;
using DazeMediaOsd.Core.Settings;

namespace DazeMediaOsd.Core.Audio;

/// <summary>
/// A device shortcut for a paired, disconnected Bluetooth device (D-023): asks it to connect, then makes it
/// the default once its endpoint turns active. One press waits at a time; a later press replaces it, and
/// any other shortcut press should <see cref="Cancel"/> it, so the newer choice wins.
/// </summary>
public sealed class DeviceConnector : IDisposable
{
    /// <summary>How long a requested device may take to show up before <see cref="TimedOut"/>.</summary>
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);

    private readonly IAudioService _audio;
    private readonly TimeProvider _time;
    private readonly ITimer _timer;
    private readonly Lock _lock = new();
    private DeviceHotkey? _pending;
    private long _armedAt;
    private bool _disposed;

    public DeviceConnector(IAudioService audio, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(audio);
        _audio = audio;
        _time = time ?? TimeProvider.System;
        _timer = _time.CreateTimer(_ => OnTimeout(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _audio.Changed += OnAudioChanged;
    }

    /// <summary>The device asked for didn't connect within <see cref="Patience"/>. Raised on a timer thread.</summary>
    public event EventHandler<DeviceHotkey>? TimedOut;

    /// <summary>Asks <paramref name="device"/> to connect; waits at most a couple of seconds for the answer.</summary>
    public ConnectResult Connect(DeviceHotkey device)
    {
        ArgumentNullException.ThrowIfNull(device);
        // Pending before asking: the endpoint may turn active before the request returns.
        lock (_lock)
        {
            _pending = device;
            _armedAt = _time.GetTimestamp();
            _timer.Change(Patience, Timeout.InfiniteTimeSpan);
        }
        var result = _audio.Connect(device.Id);
        if (result != ConnectResult.Requested)
            Clear(device);
        if (result == ConnectResult.AlreadyConnected)
            _audio.SetDefault(device.Id, device.Flow); // it came back on its own meanwhile
        return result;
    }

    /// <summary>
    /// Still waiting for this press (by reference): a "Connecting…" answer for a press that has already
    /// connected, timed out or been replaced must not be shown.
    /// </summary>
    public bool IsWaitingFor(DeviceHotkey device)
    {
        lock (_lock)
            return ReferenceEquals(_pending, device);
    }

    /// <summary>Stops waiting: the device may still connect, but won't become the default because of us.</summary>
    public void Cancel()
    {
        lock (_lock)
        {
            if (_pending is null)
                return;
            Log.Info($"connecting \"{_pending.Name}\": cancelled by a later shortcut");
            _pending = null;
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnAudioChanged(object? sender, AudioChangedEventArgs e)
    {
        if (e.Kind != AudioChangeKind.DeviceConnected || e.Device is not { } connected)
            return;
        DeviceHotkey device;
        lock (_lock)
        {
            if (_pending is null || _pending.Id != connected.Id)
                return;
            device = _pending;
            _pending = null;
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
        Log.Info($"connected \"{device.Name}\": default {device.Flow}");
        _audio.SetDefault(device.Id, device.Flow); // Windows may already have switched; this makes it certain
    }

    // The pending press and its arm time, not the timer, decide: a callback already queued for an earlier
    // press still runs after a later one re-armed.
    private void OnTimeout()
    {
        DeviceHotkey device;
        lock (_lock)
        {
            if (_pending is null || _disposed)
                return;
            var left = Patience - _time.GetElapsedTime(_armedAt);
            if (left > TimeSpan.Zero)
            {
                _timer.Change(left, Timeout.InfiniteTimeSpan);
                return;
            }
            device = _pending;
            _pending = null;
        }
        Log.Info($"connecting \"{device.Name}\" timed out after {Patience.TotalSeconds:0} s");
        TimedOut?.Invoke(this, device);
    }

    private void Clear(DeviceHotkey device)
    {
        lock (_lock)
        {
            if (!ReferenceEquals(_pending, device))
                return;
            _pending = null;
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }

    public void Dispose()
    {
        _audio.Changed -= OnAudioChanged;
        lock (_lock)
        {
            _disposed = true;
            _pending = null;
        }
        _timer.Dispose();
    }
}
