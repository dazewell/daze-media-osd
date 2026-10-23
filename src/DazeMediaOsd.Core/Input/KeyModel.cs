namespace DazeMediaOsd.Core.Input;

/// <summary>Keys that pop the OSD (D-011): volume keys and media transport keys.</summary>
public enum MediaKey
{
    VolumeUp,
    VolumeDown,
    Mute,
    PlayPause,
    NextTrack,
    PreviousTrack,
}

public sealed class MediaKeyEventArgs(MediaKey key) : EventArgs
{
    public MediaKey Key { get; } = key;
}

public interface IKeySource : IDisposable
{
    /// <summary>Raised on key down (auto-repeat included) on the source's own thread.</summary>
    event EventHandler<MediaKeyEventArgs>? KeyPressed;
}
