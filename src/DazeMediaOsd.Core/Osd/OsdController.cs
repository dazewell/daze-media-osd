using DazeMediaOsd.Core.Audio;
using DazeMediaOsd.Core.Input;

namespace DazeMediaOsd.Core.Osd;

/// <summary>Turns audio changes and volume keys into "show the OSD with this state" requests.</summary>
public sealed class OsdController : IDisposable
{
    private readonly IAudioService _audio;
    private readonly IKeySource _keys;

    public OsdController(IAudioService audio, IKeySource keys)
    {
        _audio = audio ?? throw new ArgumentNullException(nameof(audio));
        _keys = keys ?? throw new ArgumentNullException(nameof(keys));
        _audio.Changed += OnAudioChanged;
        _keys.KeyPressed += OnKeyPressed;
    }

    /// <summary>Raised on the audio or key source thread; marshal to the UI thread before rendering.</summary>
    public event EventHandler<OsdState>? ShowRequested;

    private void OnAudioChanged(object? sender, AudioChangedEventArgs e)
    {
        if (OsdRules.ReasonToShow(e) is { } reason)
            ShowRequested?.Invoke(this, OsdState.From(e.Current, reason));
    }

    // The key arrives before Windows applies it, so this state may still show the old level; the
    // VolumeChanged that follows carries the new one. At the volume limits the key is the only signal.
    private void OnKeyPressed(object? sender, VolumeKeyEventArgs e) =>
        ShowRequested?.Invoke(this, OsdState.From(_audio.Current, OsdReason.VolumeKey));

    public void Dispose()
    {
        _audio.Changed -= OnAudioChanged;
        _keys.KeyPressed -= OnKeyPressed;
    }
}
