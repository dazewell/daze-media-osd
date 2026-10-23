using DazeMediaOsd.Core.Diagnostics;
using DazeMediaOsd.Core.Settings;

namespace DazeMediaOsd.Core.Audio;

/// <summary>
/// A device shortcut for a paired, disconnected Bluetooth device (D-023): asks it to connect, then makes it
/// the default once its endpoint turns active. One press waits at a time; a later press replaces it.
/// </summary>
public sealed class DeviceConnector : IDisposable
{
    /// <summary>How long a requested device may take to show up before <see cref="TimedOut"/>.</summary>
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);

    private readonly IAudioService _audio;
    private readonly ITimer _timer;
    private readonly Lock _lock = new();
    private DeviceHotkey? _pending;

    public DeviceConnector(IAudioService audio, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(audio);
        _audio = audio;
        _timer = (time ?? TimeProvider.System).CreateTimer(_ => OnTimeout(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
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
            _timer.Change(Patience, Timeout.InfiniteTimeSpan);
        }
        var result = _audio.Connect(device.Id);
        if (result != ConnectResult.Requested)
            Clear(device);
        if (result == ConnectResult.AlreadyConnected)
            _audio.SetDefault(device.Id, device.Flow); // it came back on its own meanwhile
        return result;
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

    private void OnTimeout()
    {
        DeviceHotkey device;
        lock (_lock)
        {
            if (_pending is null)
                return;
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
        _timer.Dispose();
    }
}
