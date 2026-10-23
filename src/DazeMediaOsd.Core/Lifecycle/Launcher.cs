using System.ComponentModel;
using System.Diagnostics;
using DazeMediaOsd.Core.NativeFlyout;

namespace DazeMediaOsd.Core.Lifecycle;

/// <summary>
/// The "finally" around the app (D-006): an in-process finally doesn't run on a hard kill or native
/// crash, so a second process waits for the app and restores the native flyout however it ended.
/// </summary>
public static class Launcher
{
    private const string SingleInstanceMutex = @"Local\DazeMediaOsd.Launcher";

    public static int Run()
    {
        using var mutex = new Mutex(initiallyOwned: true, SingleInstanceMutex, out var isFirst);
        if (!isFirst)
            return 0;

        var start = new ProcessStartInfo(Environment.ProcessPath!, LaunchArgs.ForApp(Environment.ProcessId))
        {
            UseShellExecute = false,
        };
        try
        {
            using var app = Process.Start(start)!;
            app.WaitForExit();
            return app.ExitCode;
        }
        finally
        {
            FlyoutHost.RestoreAll();
        }
    }

    /// <summary>
    /// Watches the launcher. Returns its handle (keep it alive) when it is alive and watched; then
    /// <paramref name="onLost"/> runs once, on a pool thread, when it exits. Returns null when there is
    /// no live launcher to watch: then <paramref name="onLost"/> has already run, and the caller must not
    /// minimize the host (nothing would restore it after a crash).
    /// </summary>
    public static Process? WatchLauncher(int? launcherPid, Action onLost)
    {
        ArgumentNullException.ThrowIfNull(onLost);
        var fired = 0;
        void LostOnce()
        {
            if (Interlocked.Exchange(ref fired, 1) == 0)
                onLost();
        }

        var launcher = TryWatch(launcherPid, LostOnce);
        if (launcher is null)
            LostOnce();
        return launcher;
    }

    private static Process? TryWatch(int? launcherPid, Action onLost)
    {
        if (launcherPid is not int pid)
            return null;
        Process? launcher = null;
        try
        {
            launcher = Process.GetProcessById(pid);
            using (var self = Process.GetCurrentProcess())
            {
                // A recycled pid would be a process started after us.
                if (launcher.StartTime > self.StartTime)
                {
                    launcher.Dispose();
                    return null;
                }
            }
            // Subscribe before enabling, then re-check: an exit in between still reaches onLost.
            launcher.Exited += (_, _) => onLost();
            launcher.EnableRaisingEvents = true;
            if (launcher.HasExited)
            {
                launcher.Dispose();
                return null;
            }
            return launcher;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or Win32Exception)
        {
            // Exited or recycled while we were querying it: same as no launcher.
            launcher?.Dispose();
            return null;
        }
    }
}
