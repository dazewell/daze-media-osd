using DazeMediaOsd.Core.Audio;
using DazeMediaOsd.Core.Input;
using DazeMediaOsd.Core.Media;

namespace DazeMediaOsd.Core.Osd;

/// <summary>Turns audio changes, keys and media session changes into OSD show/refresh requests.</summary>
public sealed class OsdController : IDisposable
{
    /// <summary>
    /// One press can arrive twice (keyboard and Consumer Control paths) with identical state; drop an
    /// identical request within this window. Held keys still re-request after it, keeping the OSD up.
    /// </summary>
    internal static readonly TimeSpan CoalesceWindow = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// A device connect/disconnect shows once this long after the last one (D-013): one Bluetooth headset is
    /// several endpoints, and the default switch that often follows must land first so the OSD shows it.
    /// </summary>
    internal static readonly TimeSpan DeviceSettle = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// A burst of the same device doing the same thing that starts this soon after it was announced is a
    /// straggler endpoint, not news (D-016). Measured from the announce, which a default switch makes at once.
    /// </summary>
    internal static readonly TimeSpan RepeatWindow = TimeSpan.FromSeconds(2);

    private readonly IAudioService _audio;
    private readonly IKeySource _keys;
    private readonly IMediaService _media;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private OsdState? _lastRequested;
    private long _lastRequestedAt;
    // Held across the decide-and-request of a device or default-device show, so a pending device show is
    // either requested before a default switch or not at all. Always taken before _gate.
    private readonly Lock _deviceGate = new();
    private ITimer? _deviceTimer;
    private OsdReason _deviceReason;
    private readonly List<DeviceEvent> _deviceEvents = [];
    private bool _devicePending;
    private long _deviceArmedAt;
    private long _burstStartedAt;
    private DeviceNotice? _announced;
    private long _announcedAt;
    private bool _disposed;
    private MediaState? _lastMedia;

    public OsdController(IAudioService audio, IKeySource keys, IMediaService media, TimeProvider? time = null)
    {
        _audio = audio ?? throw new ArgumentNullException(nameof(audio));
        _keys = keys ?? throw new ArgumentNullException(nameof(keys));
        _media = media ?? throw new ArgumentNullException(nameof(media));
        _time = time ?? TimeProvider.System;
        _lastMedia = _media.Current;
        _audio.Changed += OnAudioChanged;
        _keys.KeyPressed += OnKeyPressed;
        _media.Changed += OnMediaChanged;
    }

    /// <summary>Raised on the audio, key or media thread; marshal to the UI thread before rendering.</summary>
    public event EventHandler<OsdState>? ShowRequested;

    /// <summary>
    /// The media session changed: re-render the OSD if it is visible, without popping it or extending its
    /// time. A media key arrives before the player reacts, so this carries the new track or play state.
    /// Raised on the media thread.
    /// </summary>
    public event EventHandler<OsdState>? RefreshRequested;

    /// <summary>The state as it is now, for a show that doesn't come from a Windows signal.</summary>
    public OsdState Snapshot(OsdReason reason) => OsdState.From(_audio.Current, _media.Current, reason);

    private void OnAudioChanged(object? sender, AudioChangedEventArgs e)
    {
        switch (OsdRules.ReasonToShow(e))
        {
            case (OsdReason.DeviceConnected or OsdReason.DeviceDisconnected) and var reason:
                DeferDeviceShow(reason, e.Device);
                break;
            case (OsdReason.OutputDeviceChanged or OsdReason.InputDeviceChanged) and var reason:
                // It shows the new default right away; a pending device show would only repeat it, so this
                // show announces the device instead (D-014).
                lock (_deviceGate)
                {
                    var device = _devicePending ? TakeDeviceNotice() : null;
                    _devicePending = false;
                    if (device is not null)
                        (_announced, _announcedAt) = (device, _time.GetTimestamp());
                    Request(OsdState.From(e.Current, _media.Current, reason) with { Device = device });
                }
                break;
            case { } reason:
                Request(OsdState.From(e.Current, _media.Current, reason));
                break;
        }
    }

    private void DeferDeviceShow(OsdReason reason, DeviceEvent? device)
    {
        lock (_deviceGate)
        {
            if (_disposed)
                return;
            if (!_devicePending)
            {
                _deviceEvents.Clear(); // a new burst
                _burstStartedAt = _time.GetTimestamp();
            }
            if (device is not null)
                _deviceEvents.Add(device);
            _deviceReason = reason; // the last one of a burst names it
            _devicePending = true;
            _deviceArmedAt = _time.GetTimestamp();
            _deviceTimer ??= _time.CreateTimer(_ => OnDeviceSettled(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _deviceTimer.Change(DeviceSettle, Timeout.InfiniteTimeSpan);
        }
    }

    // Reads the state now, after the burst: the current defaults, plus the device(s) that came or went.
    // The flags, not the timer, decide: a callback already queued when the show was cancelled, re-armed
    // or disposed still runs.
    private void OnDeviceSettled()
    {
        lock (_deviceGate)
        {
            if (!_devicePending || _disposed)
                return;
            var left = DeviceSettle - _time.GetElapsedTime(_deviceArmedAt);
            if (left > TimeSpan.Zero)
            {
                _deviceTimer!.Change(left, Timeout.InfiniteTimeSpan); // stale callback from before a re-arm
                return;
            }
            _devicePending = false;
            // A flap (went and came back) is news even though it ends where the announce left off.
            var oneWay = _deviceEvents.All(e => e.Connected == _deviceEvents[0].Connected);
            var device = TakeDeviceNotice();
            if (oneWay && RepeatsAnnounced(device))
                return;
            if (device is not null)
                (_announced, _announcedAt) = (device, _time.GetTimestamp());
            Request(Snapshot(_deviceReason) with { Device = device });
        }
    }

    // A Bluetooth headset's second endpoint often comes 30–930 ms after the first, after that one's show (a
    // default switch, or a settled burst) already announced the device: a burst that began within
    // RepeatWindow of announcing the same device doing the same thing only repeats it (D-014, D-016).
    private bool RepeatsAnnounced(DeviceNotice? device) =>
        device is not null
        && _announced is { } announced
        && string.Equals(announced.Name, device.Name, StringComparison.OrdinalIgnoreCase)
        && announced.Connected == device.Connected
        && _time.GetElapsedTime(_announcedAt, _burstStartedAt) < RepeatWindow;

    // Caller holds _deviceGate.
    private DeviceNotice? TakeDeviceNotice()
    {
        var notice = DeviceNotice.Summarize(_deviceEvents);
        _deviceEvents.Clear();
        return notice;
    }

    // The key arrives before Windows applies it, so this state may still show the old level; the
    // VolumeChanged that follows carries the new one. At the volume limits the key is the only signal.
    private void OnKeyPressed(object? sender, MediaKeyEventArgs e)
    {
        var media = _media.Current;
        if (OsdRules.ReasonToShow(e.Key, media) is { } reason)
            Request(OsdState.From(_audio.Current, media, reason));
    }

    // The show goes out with every qualifying change; App turns it back into a refresh while the setting is
    // off, so this needs no settings (D-032).
    private void OnMediaChanged(object? sender, MediaState? media)
    {
        var before = Interlocked.Exchange(ref _lastMedia, media);
        if (OsdRules.ReasonToShow(before, media) is { } reason)
            Request(OsdState.From(_audio.Current, media, reason));
        else
            RefreshRequested?.Invoke(this, OsdState.From(_audio.Current, media, OsdReason.MediaChanged));
    }

    // Audio and key sources call in from different threads.
    private void Request(OsdState state)
    {
        lock (_gate)
        {
            var now = _time.GetTimestamp();
            if (state == _lastRequested && _time.GetElapsedTime(_lastRequestedAt, now) < CoalesceWindow)
                return;
            (_lastRequested, _lastRequestedAt) = (state, now);
        }
        ShowRequested?.Invoke(this, state);
    }

    public void Dispose()
    {
        _audio.Changed -= OnAudioChanged;
        _keys.KeyPressed -= OnKeyPressed;
        _media.Changed -= OnMediaChanged;
        lock (_deviceGate)
        {
            _disposed = true;
            _deviceTimer?.Dispose();
            _deviceTimer = null;
        }
    }
}
