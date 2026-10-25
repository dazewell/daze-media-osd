using System.ComponentModel;
using System.Diagnostics;
using DazeMediaOsd.Core.Diagnostics;
using DazeMediaOsd.Core.NativeFlyout;

namespace DazeMediaOsd.Core.Lifecycle;

/// <summary>
/// The "finally" around the app (D-006): an in-process finally doesn't run on a hard kill or native
/// crash, so a second process waits for the app and restores the native flyout however it ended.
/// </summary>
public static class Launcher
{
    private const string SingleInstanceMutex = @"Local\DazeMediaOsd.Launcher";
    private const string AppInstanceMutex = @"Local\DazeMediaOsd.App";

    public static int Run()
    {
        using var mutex = new Mutex(initiallyOwned: true, SingleInstanceMutex, out var isFirst);
        if (!isFirst)
        {
            Log.Info("launcher: another instance is running, exiting");
            return 0;
        }

        var start = new ProcessStartInfo(Environment.ProcessPath!, LaunchArgs.ForApp(Environment.ProcessId))
        {
            UseShellExecute = false,
        };
        try
        {
            using var app = Process.Start(start)!;
            Log.Info($"launcher: started app {app.Id}, version {AppVersion.Current}, {Environment.ProcessPath}");
            app.WaitForExit();
            Log.Info($"launcher: app exited with {app.ExitCode}, restoring native flyout");
            return app.ExitCode;
        }
        finally
        {
            FlyoutHost.RestoreAll();
        }
    }

    /// <summary>
    /// Single instance for the app process itself. The launcher's mutex dies with the launcher, while an
    /// orphaned app (launcher killed) keeps running; without this the next launch would add a second
    /// app and a second OSD. Dispose the returned mutex when the app exits; null means another app runs.
    /// </summary>
    public static Mutex? ClaimAppInstance()
    {
        var mutex = new Mutex(initiallyOwned: true, AppInstanceMutex, out var isFirst);
        if (isFirst)
            return mutex;
        mutex.Dispose();
        return null;
    }

    /// <summary>True once neither a launcher nor an app holds its mutex, or false after <paramref name="timeout"/>.</summary>
    public static bool WaitUntilStopped(TimeSpan timeout)
    {
        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (IsHeld(SingleInstanceMutex) || IsHeld(AppInstanceMutex))
        {
            if (Environment.TickCount64 >= deadline)
                return false;
            Thread.Sleep(100);
        }
        return true;
    }

    private static bool IsHeld(string name)
    {
        if (!Mutex.TryOpenExisting(name, out var mutex))
            return false;
        mutex.Dispose();
        return true;
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
                // A recycled pid would be another program, or a process started after us.
                if (launcher.ProcessName != self.ProcessName || launcher.StartTime > self.StartTime)
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
