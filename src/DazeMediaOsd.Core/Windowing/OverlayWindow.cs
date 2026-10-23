using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.UI.WindowsAndMessaging;

namespace DazeMediaOsd.Core.Windowing;

/// <summary>Win32 styling for the OSD: topmost, never activated, click-through, not in Alt+Tab or the taskbar.</summary>
public static class OverlayWindow
{
    /// <summary>Applies the overlay styles once, before the window is first shown.</summary>
    public static unsafe void Apply(nint hwnd)
    {
        var h = (HWND)hwnd;
        var ex = (WINDOW_EX_STYLE)PInvoke.GetWindowLong(h, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        ex |= WINDOW_EX_STYLE.WS_EX_NOACTIVATE | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_TOPMOST
            // Click-through: WS_EX_TRANSPARENT only passes hits to other windows on a layered window.
            | WINDOW_EX_STYLE.WS_EX_TRANSPARENT | WINDOW_EX_STYLE.WS_EX_LAYERED;
        ex &= ~WINDOW_EX_STYLE.WS_EX_APPWINDOW;
        // Returns the previous style; failure would surface as the layered call below failing.
        _ = PInvoke.SetWindowLong(h, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, (int)ex);
        // A layered window without attributes is invisible; fully opaque keeps the composed content as is.
        if (!PInvoke.SetLayeredWindowAttributes(h, new COLORREF(0), 255, LAYERED_WINDOW_ATTRIBUTES_FLAGS.LWA_ALPHA))
            throw new InvalidOperationException("SetLayeredWindowAttributes failed; the OSD would stay invisible");

        var corners = DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND;
        // Cosmetic: square corners are no reason to fail.
        _ = PInvoke.DwmSetWindowAttribute(h, DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE, &corners, sizeof(DWM_WINDOW_CORNER_PREFERENCE));
    }

    /// <summary>
    /// Shows at the given physical-pixel bounds without activating (the OSD must never take focus).
    /// False if Windows refused, i.e. no OSD is visible.
    /// </summary>
    public static bool ShowAt(nint hwnd, int x, int y, int width, int height) =>
        PInvoke.SetWindowPos((HWND)hwnd, HWND.HWND_TOPMOST, x, y, width, height,
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW);

    /// <summary>Window size minus client size, in physical pixels: what the non-client frame takes.</summary>
    public static (int Width, int Height) FrameSize(nint hwnd)
    {
        if (!PInvoke.GetWindowRect((HWND)hwnd, out var window) || !PInvoke.GetClientRect((HWND)hwnd, out var client))
            return (0, 0);
        return (window.right - window.left - (client.right - client.left), window.bottom - window.top - (client.bottom - client.top));
    }

    /// <summary>Whole-window opacity (0–255) of the layered window: content, acrylic and frame alike.</summary>
    public static bool SetAlpha(nint hwnd, byte alpha) =>
        PInvoke.SetLayeredWindowAttributes((HWND)hwnd, new COLORREF(0), alpha, LAYERED_WINDOW_ATTRIBUTES_FLAGS.LWA_ALPHA);

    /// <summary>Windows Settings › Accessibility › Visual effects › "Animation effects". Read per show: it can change any time.</summary>
    public static unsafe bool AnimationsEnabled()
    {
        BOOL enabled = true;
        return !PInvoke.SystemParametersInfo(SYSTEM_PARAMETERS_INFO_ACTION.SPI_GETCLIENTAREAANIMATION, 0, &enabled, 0) || enabled;
    }

    public static void Hide(nint hwnd) => _ = PInvoke.ShowWindow((HWND)hwnd, SHOW_WINDOW_CMD.SW_HIDE);

    /// <summary>Device pixels per DIP for the monitor the window is on.</summary>
    public static double Scale(nint hwnd) => PInvoke.GetDpiForWindow((HWND)hwnd) / 96.0;
}
