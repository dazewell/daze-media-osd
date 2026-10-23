using System.Globalization;

namespace DazeMediaOsd.Core.Lifecycle;

public enum LaunchMode
{
    /// <summary>No arguments: start the app as a child and restore the native flyout when it exits (D-006).</summary>
    Launcher,

    /// <summary><c>--app</c>: the tray/OSD process.</summary>
    App,

    /// <summary><c>--restore</c>: un-minimize the native flyout and exit (recovery, stop script).</summary>
    Restore,
}

public sealed record LaunchArgs(LaunchMode Mode, int? LauncherPid)
{
    public const string AppSwitch = "--app";
    public const string RestoreSwitch = "--restore";
    public const string LauncherPidSwitch = "--launcher-pid";

    public static LaunchArgs Parse(IReadOnlyList<string> args)
    {
        var mode = LaunchMode.Launcher;
        int? launcherPid = null;
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i] == AppSwitch)
                mode = LaunchMode.App;
            else if (args[i] == RestoreSwitch)
                mode = LaunchMode.Restore;
            else if (args[i] == LauncherPidSwitch && i + 1 < args.Count
                     && int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var pid))
                launcherPid = pid;
        }
        return new LaunchArgs(mode, launcherPid);
    }

    public static string ForApp(int launcherPid) =>
        string.Create(CultureInfo.InvariantCulture, $"{AppSwitch} {LauncherPidSwitch} {launcherPid}");
}
