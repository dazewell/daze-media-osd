using System.Diagnostics;
using DazeMediaOsd.Core.Interop;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace DazeMediaOsd.Core.NativeFlyout;

/// <summary>Stateless operations on explorer's volume flyout host window.</summary>
public static class FlyoutHost
{
    /// <summary>All current flyout hosts (normally one; more only while explorer restarts).</summary>
    public static IReadOnlyList<nint> FindAll()
    {
        var hosts = new List<nint>();
        var hwnd = HWND.Null;
        while ((hwnd = PInvoke.FindWindowEx(HWND.Null, hwnd, FlyoutHostMatcher.HostClass, null)) != HWND.Null)
        {
            if (IsFlyoutHost(hwnd))
                hosts.Add(hwnd);
        }
        return hosts;
    }

    internal static bool IsFlyoutHost(HWND hwnd) =>
        FlyoutHostMatcher.IsFlyoutHost(ClassName(hwnd), ProcessName(hwnd), Band(hwnd));

    internal static bool IsMinimized(HWND hwnd) => PInvoke.IsIconic(hwnd);

    /// <summary>Keeps the native flyout from drawing at all (D-006). Survives our process: always pair with <see cref="Restore"/>.</summary>
    internal static void Minimize(HWND hwnd)
    {
        var content = PInvoke.FindWindowEx(hwnd, HWND.Null, (string?)null, null);
        if (content != HWND.Null)
            PInvoke.ShowWindowAsync(content, SHOW_WINDOW_CMD.SW_MINIMIZE);
        PInvoke.ShowWindowAsync(hwnd, SHOW_WINDOW_CMD.SW_MINIMIZE);
        PInvoke.ShowWindowAsync(hwnd, SHOW_WINDOW_CMD.SW_FORCEMINIMIZE);
    }

    /// <summary>Back to stock: un-minimize content and host, then hide the host as explorer keeps it between shows.</summary>
    internal static void Restore(HWND hwnd)
    {
        var content = PInvoke.FindWindowEx(hwnd, HWND.Null, (string?)null, null);
        if (content != HWND.Null)
            PInvoke.ShowWindowAsync(content, SHOW_WINDOW_CMD.SW_RESTORE);
        PInvoke.ShowWindowAsync(hwnd, SHOW_WINDOW_CMD.SW_RESTORE);
        PInvoke.ShowWindowAsync(hwnd, SHOW_WINDOW_CMD.SW_HIDE);
    }

    internal static void Hide(HWND hwnd) => PInvoke.ShowWindowAsync(hwnd, SHOW_WINDOW_CMD.SW_HIDE);

    /// <summary>Restores every host. Safe to call any number of times, from any thread, when nothing is minimized.</summary>
    public static void RestoreAll()
    {
        foreach (var host in FindAll())
            Restore((HWND)host);
    }

    internal static unsafe string ClassName(HWND hwnd)
    {
        Span<char> buffer = stackalloc char[256];
        fixed (char* p = buffer)
        {
            var length = PInvoke.GetClassName(hwnd, p, buffer.Length);
            return new string(buffer[..length]);
        }
    }

    private static string ProcessName(HWND hwnd)
    {
        unsafe
        {
            uint pid;
            if (PInvoke.GetWindowThreadProcessId(hwnd, &pid) == 0)
                return "";
            try
            {
                using var process = Process.GetProcessById((int)pid);
                return process.ProcessName;
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException)
            {
                // Process gone (normal during an explorer restart): a stale window is no match.
                return "";
            }
        }
    }

    private static uint Band(HWND hwnd) => UndocumentedMethods.GetWindowBand(hwnd, out var band) ? band : 0;
}
