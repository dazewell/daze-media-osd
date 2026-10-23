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

    private readonly IAudioService _audio;
    private readonly IKeySource _keys;
    private readonly IMediaService _media;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private OsdState? _lastRequested;
    private long _lastRequestedAt;

    public OsdController(IAudioService audio, IKeySource keys, IMediaService media, TimeProvider? time = null)
    {
        _audio = audio ?? throw new ArgumentNullException(nameof(audio));
        _keys = keys ?? throw new ArgumentNullException(nameof(keys));
        _media = media ?? throw new ArgumentNullException(nameof(media));
        _time = time ?? TimeProvider.System;
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
        if (OsdRules.ReasonToShow(e) is { } reason)
            Request(OsdState.From(e.Current, _media.Current, reason));
    }

    // The key arrives before Windows applies it, so this state may still show the old level; the
    // VolumeChanged that follows carries the new one. At the volume limits the key is the only signal.
    private void OnKeyPressed(object? sender, MediaKeyEventArgs e)
    {
        var media = _media.Current;
        if (OsdRules.ReasonToShow(e.Key, media) is { } reason)
            Request(OsdState.From(_audio.Current, media, reason));
    }

    private void OnMediaChanged(object? sender, MediaState? media) =>
        RefreshRequested?.Invoke(this, OsdState.From(_audio.Current, media, OsdReason.MediaChanged));

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
    }
}
