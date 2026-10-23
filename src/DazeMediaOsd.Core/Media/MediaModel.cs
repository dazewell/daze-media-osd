namespace DazeMediaOsd.Core.Media;

/// <summary>
/// The current media session (what the native flyout's media panel shows). <paramref name="Thumbnail"/> is
/// the encoded image (PNG/JPEG) as the player supplied it, or null. Records compare arrays by reference:
/// <see cref="GsmtcMediaService"/> keeps the same array while the image is unchanged.
/// </summary>
public sealed record MediaState(string AppId, string Title, string Artist, bool IsPlaying, byte[]? Thumbnail);

public interface IMediaService : IDisposable
{
    /// <summary>Null while no media session is active.</summary>
    MediaState? Current { get; }

    /// <summary>Raised on a thread-pool thread whenever <see cref="Current"/> changes; marshal before touching UI.</summary>
    event EventHandler<MediaState?>? Changed;
}
