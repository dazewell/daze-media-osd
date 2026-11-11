using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace DazeMediaOsd.Core.Windowing;

/// <summary>
/// Raises <see cref="Changed"/> after the window's DPI, the display mode or the work area changed (scaling,
/// resolution, a monitor plugged in or out, the taskbar resized). Subclasses the window: WinUI keeps its own
/// handling, ours runs after it. Raised on the window's thread, inside the message: defer real work. Lives as
/// long as the window: removes itself on WM_NCDESTROY.
/// </summary>
public sealed class DisplayChangeHook
{
    private const nuint SubclassId = 0x0D5D;
    private readonly SUBCLASSPROC _proc; // comctl32 holds only a native pointer: keep the delegate alive

    public DisplayChangeHook(nint hwnd)
    {
        _proc = Proc;
        if (!PInvoke.SetWindowSubclass((HWND)hwnd, _proc, SubclassId, 0))
            throw new InvalidOperationException("SetWindowSubclass failed");
    }

    public event EventHandler<string>? Changed;

    private LRESULT Proc(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam, nuint id, nuint data)
    {
        if (message == PInvoke.WM_NCDESTROY)
            _ = PInvoke.RemoveWindowSubclass(hwnd, _proc, SubclassId);
        var result = PInvoke.DefSubclassProc(hwnd, message, wParam, lParam);
        var reason = message switch
        {
            PInvoke.WM_DPICHANGED => $"dpi {(ushort)wParam.Value}",
            PInvoke.WM_DISPLAYCHANGE => $"display {(ushort)lParam.Value}x{(ushort)((nuint)lParam.Value >> 16)}",
            PInvoke.WM_SETTINGCHANGE when wParam.Value == (nuint)SYSTEM_PARAMETERS_INFO_ACTION.SPI_SETWORKAREA => "work area",
            _ => null,
        };
        if (reason is not null)
            Changed?.Invoke(this, reason);
        return result;
    }
}
