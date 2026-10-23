using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace DazeMediaOsd.Core.Windowing;

/// <summary>For ordinary windows (settings), never the OSD.</summary>
public static class WindowActivation
{
    /// <summary>
    /// Restores and foregrounds an already open window. WinUI's <c>Window.Activate()</c> alone leaves it
    /// behind other apps. Works after a tray click or menu pick: the shell lets our process take focus then.
    /// </summary>
    public static void BringToFront(nint hwnd)
    {
        var window = (HWND)hwnd;
        if (PInvoke.IsIconic(window))
            PInvoke.ShowWindow(window, SHOW_WINDOW_CMD.SW_RESTORE);
        PInvoke.SetForegroundWindow(window);
    }
}
