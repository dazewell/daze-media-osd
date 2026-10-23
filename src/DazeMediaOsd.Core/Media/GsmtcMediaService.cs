using System.Runtime.InteropServices;
using DazeMediaOsd.Core.Diagnostics;
using Windows.Media.Control;
using Windows.Storage.Streams;
using Session = Windows.Media.Control.GlobalSystemMediaTransportControlsSession;
using SessionManager = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager;

namespace DazeMediaOsd.Core.Media;

/// <summary>
/// Follows the system's current media session through GSMTC, the API behind the native flyout's media
/// panel. Starts in the background: the OSD works without media info, so a failure is only logged.
/// Every GSMTC event just marks the state dirty; one refresh at a time re-reads the current session.
/// </summary>
public sealed class GsmtcMediaService : IMediaService
{
    // Players hand over cover art of a few hundred KB at most; refuse anything absurd.
    private const uint MaxThumbnailBytes = 4 * 1024 * 1024;
    private const int MaxRetries = 5;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(200);

    private readonly Lock _gate = new();
    private SessionManager? _manager;
    private Session? _session; // the one whose events we're subscribed to; refresh loop only
    private int _refreshing;
    private int _dirty;
    private volatile bool _disposed;

    public GsmtcMediaService() => _ = Task.Run(StartAsync);

    public MediaState? Current { get; private set; }

    public event EventHandler<MediaState?>? Changed;

    private async Task StartAsync()
    {
        try
        {
            var manager = await SessionManager.RequestAsync();
            lock (_gate)
            {
                if (_disposed)
                    return;
                _manager = manager;
                manager.CurrentSessionChanged += OnSessionEvent;
                manager.SessionsChanged += OnSessionEvent;
            }
            ScheduleRefresh();
        }
        catch (Exception e) when (e is COMException or InvalidOperationException or UnauthorizedAccessException)
        {
            Log.Error("media sessions unavailable; the OSD shows no media info", e);
        }
    }

    private void OnSessionEvent(SessionManager sender, object args) => ScheduleRefresh();

    private void OnMediaPropertiesChanged(Session sender, MediaPropertiesChangedEventArgs args) => ScheduleRefresh();

    private void OnPlaybackInfoChanged(Session sender, PlaybackInfoChangedEventArgs args) => ScheduleRefresh();

    // Events arrive on arbitrary threads, often several per track change: coalesce them into one loop
    // that re-reads until nothing new came in while it ran.
    private void ScheduleRefresh()
    {
        Volatile.Write(ref _dirty, 1);
        if (Interlocked.CompareExchange(ref _refreshing, 1, 0) == 0)
            _ = Task.Run(RefreshLoopAsync);
    }

    private async Task RefreshLoopAsync()
    {
        try
        {
            var failures = 0;
            while (!_disposed && Interlocked.Exchange(ref _dirty, 0) == 1)
            {
                if (await RefreshAsync())
                {
                    failures = 0;
                }
                else if (++failures <= MaxRetries)
                {
                    await Task.Delay(RetryDelay);
                    Volatile.Write(ref _dirty, 1);
                }
            }
        }
        finally
        {
            Volatile.Write(ref _refreshing, 0);
            // An event between the loop's last check and the flag reset would otherwise be lost.
            if (Volatile.Read(ref _dirty) == 1 && !_disposed)
                ScheduleRefresh();
        }
    }

    /// <returns>False if the session couldn't be read; <see cref="Current"/> is then left as it was.</returns>
    private async Task<bool> RefreshAsync()
    {
        MediaState? next;
        try
        {
            var session = _manager?.GetCurrentSession();
            Follow(session);
            next = session is null ? null : await ReadAsync(session, Current);
        }
        // Seen on 26300: TryGetMediaPropertiesAsync fails with RPC_E_WRONG_THREAD (0x8001010E) right after
        // a Chromium player pauses, and no further event follows. Publishing "no session" then would hide
        // the media row until the next track, so keep the last state and let the loop retry.
        catch (Exception e) when (e is COMException or InvalidOperationException or ObjectDisposedException)
        {
            Log.Info($"reading the media session failed (0x{e.HResult:X8}), will retry");
            return false;
        }
        if (_disposed || next == Current)
            return true;
        Current = next;
        Log.Info(next is null
            ? "media: no session"
            : $"media: {next.AppId} \"{next.Artist}\" - \"{next.Title}\" {(next.IsPlaying ? "playing" : "paused")}, thumbnail {next.Thumbnail?.Length ?? 0} B");
        try
        {
            Changed?.Invoke(this, next);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Log.Error("media change handler", e);
        }
        return true;
    }

    private void Follow(Session? session)
    {
        if (ReferenceEquals(session, _session))
            return;
        if (_session is not null)
        {
            _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
        }
        _session = session;
        if (session is not null)
        {
            session.MediaPropertiesChanged += OnMediaPropertiesChanged;
            session.PlaybackInfoChanged += OnPlaybackInfoChanged;
        }
    }

    private static async Task<MediaState?> ReadAsync(Session session, MediaState? previous)
    {
        var properties = await session.TryGetMediaPropertiesAsync();
        var playback = session.GetPlaybackInfo();
        var title = properties?.Title ?? "";
        // A session without a title (a player between tracks, a tab with no media) has nothing to show.
        if (title.Length == 0)
            return null;
        var thumbnail = properties?.Thumbnail is { } reference ? await ReadThumbnailAsync(reference) : null;
        // Same bytes → same array, so an unchanged session compares equal (no change event, OSD coalescing).
        if (thumbnail is not null && previous?.Thumbnail is { } old && thumbnail.AsSpan().SequenceEqual(old))
            thumbnail = old;
        return new MediaState(
            session.SourceAppUserModelId ?? "",
            title,
            properties?.Artist ?? "",
            playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
            thumbnail);
    }

    private static async Task<byte[]?> ReadThumbnailAsync(IRandomAccessStreamReference reference)
    {
        try
        {
            using var stream = await reference.OpenReadAsync();
            if (stream.Size is 0 or > MaxThumbnailBytes)
                return null;
            using var reader = new DataReader(stream.GetInputStreamAt(0));
            var size = (uint)stream.Size;
            await reader.LoadAsync(size);
            var bytes = new byte[size];
            reader.ReadBytes(bytes);
            return bytes;
        }
        // Some players hand out a reference that fails to open; show the track without art.
        catch (Exception e) when (e is COMException or InvalidOperationException or UnauthorizedAccessException or ArgumentException)
        {
            Log.Error("reading the media thumbnail failed", e);
            return null;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            if (_manager is not null)
            {
                _manager.CurrentSessionChanged -= OnSessionEvent;
                _manager.SessionsChanged -= OnSessionEvent;
            }
        }
        // A refresh still running only reads and publishes; Changed handlers ignore it after exit.
        var session = _session;
        if (session is not null)
        {
            session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
        }
    }
}
