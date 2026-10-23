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
    /// Calls <paramref name="onLost"/> (on a pool thread) once the launcher is gone, immediately if it
    /// already is. Returns the handle to keep alive, or null.
    /// </summary>
    public static Process? WatchLauncher(int? launcherPid, Action onLost)
    {
        ArgumentNullException.ThrowIfNull(onLost);
        Process? launcher = null;
        try
        {
            if (launcherPid is int pid)
            {
                launcher = Process.GetProcessById(pid);
                // A recycled pid would be a process started after us.
                if (launcher.StartTime > Process.GetCurrentProcess().StartTime)
                    launcher = null;
            }
        }
        catch (ArgumentException)
        {
            launcher = null;
        }

        if (launcher is null)
        {
            onLost();
            return null;
        }
        launcher.EnableRaisingEvents = true;
        launcher.Exited += (_, _) => onLost();
        if (launcher.HasExited) // exited between GetProcessById and subscribing
            onLost();
        return launcher;
    }
}
