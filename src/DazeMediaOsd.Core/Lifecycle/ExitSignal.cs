namespace DazeMediaOsd.Core.Lifecycle;

/// <summary>
/// A named event another process sets to make the running app exit the way tray Exit does, so the
/// launcher restores the native flyout (D-006). Velopack's before-update hook uses it (D-029): the
/// updater would otherwise kill launcher and app together and leave the flyout minimized.
/// </summary>
public sealed class ExitSignal : IDisposable
{
    public const string DefaultName = @"Local\DazeMediaOsd.Exit";

    private readonly EventWaitHandle _event;
    private readonly RegisteredWaitHandle _wait;

    private ExitSignal(string name, Action onRequested)
    {
        _event = new EventWaitHandle(false, EventResetMode.AutoReset, name);
        _wait = ThreadPool.RegisterWaitForSingleObject(_event, (_, _) => onRequested(), null, Timeout.Infinite, executeOnlyOnce: true);
    }

    /// <summary>Runs <paramref name="onRequested"/> once, on a pool thread, when another process calls <see cref="Request"/>.</summary>
    public static ExitSignal Listen(Action onRequested, string name = DefaultName)
    {
        ArgumentNullException.ThrowIfNull(onRequested);
        return new ExitSignal(name, onRequested);
    }

    /// <summary>Asks a listening app to exit. False when no app listens (nothing running).</summary>
    public static bool Request(string name = DefaultName)
    {
        if (!EventWaitHandle.TryOpenExisting(name, out var handle))
            return false;
        using (handle)
            handle.Set();
        return true;
    }

    public void Dispose()
    {
        _wait.Unregister(null);
        _event.Dispose();
    }
}
