using System.Diagnostics;
using System.Runtime.InteropServices;
using DazeMediaOsd.Core.Diagnostics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.Input;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace DazeMediaOsd.Core.Input;

/// <summary>
/// Sees mouse wheel turns over a taskbar passively through raw input (RIDEV_INPUTSINK), like
/// <see cref="RawInputKeySource"/>: no hook, nobody's input waits on us (D-035). Its own thread and message-only
/// window, so the stream of mouse-move messages never delays the key source. Lookups here (the speaker icon's
/// place through UIA) only delay our own answer, never the system's input.
/// </summary>
public sealed class TaskbarWheelSource : ITaskbarWheelSource
{
    private const string WindowClass = "DazeMediaOsd.TaskbarWheel";

    private readonly Thread _thread;
    private readonly WNDPROC _wndProc; // the window class holds only a native pointer: keep the delegate alive
    private readonly VolumeIconLocator _volumeIcon;
    private HWND _window;

    /// <param name="outputName">The default output's name, to find the speaker icon by its tooltip.</param>
    /// <exception cref="InvalidOperationException">The window or the raw input registration failed.</exception>
    public TaskbarWheelSource(Func<string?> outputName)
    {
        _volumeIcon = new VolumeIconLocator(outputName ?? throw new ArgumentNullException(nameof(outputName)));
        _wndProc = WndProc;
        using var ready = new ManualResetEventSlim();
        Exception? failure = null;
        _thread = new Thread(() =>
        {
            try
            {
                CreateWindow();
            }
            catch (InvalidOperationException e)
            {
                failure = e;
            }
            finally
            {
                ready.Set();
            }
            if (failure is null)
                RunMessageLoop();
        })
        {
            IsBackground = true,
            Name = "TaskbarWheel",
        };
        _thread.SetApartmentState(ApartmentState.MTA); // UIA clients belong in the MTA
        _thread.Start();
        ready.Wait();
        if (failure is not null)
            throw failure;
    }

    public event EventHandler<TaskbarWheelEventArgs>? Wheeled;

    private unsafe void CreateWindow()
    {
        var instance = (HINSTANCE)(nint)PInvoke.GetModuleHandle((PCWSTR)null).Value;
        fixed (char* className = WindowClass)
        {
            var windowClass = new WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                lpfnWndProc = _wndProc,
                hInstance = instance,
                lpszClassName = className,
            };
            if (PInvoke.RegisterClassEx(windowClass) == 0)
                throw new InvalidOperationException("RegisterClassEx failed.");
            _window = PInvoke.CreateWindowEx(0, className, null, 0, 0, 0, 0, 0, HWND.HWND_MESSAGE, HMENU.Null, instance, null);
        }
        if (_window == HWND.Null)
            throw new InvalidOperationException("CreateWindowEx failed.");

        // Generic desktop / mouse. One registration per device class per process: the key source takes others.
        ReadOnlySpan<RAWINPUTDEVICE> devices =
        [
            new() { usUsagePage = 0x01, usUsage = 0x02, dwFlags = RAWINPUTDEVICE_FLAGS.RIDEV_INPUTSINK, hwndTarget = _window },
        ];
        if (!PInvoke.RegisterRawInputDevices(devices, (uint)sizeof(RAWINPUTDEVICE)))
            throw new InvalidOperationException("RegisterRawInputDevices (mouse) failed.");
    }

    private static void RunMessageLoop()
    {
        while (PInvoke.GetMessage(out var message, HWND.Null, 0, 0) > 0)
            PInvoke.DispatchMessage(message);
    }

    private LRESULT WndProc(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam)
    {
        switch (message)
        {
            case PInvoke.WM_INPUT:
                if (ReadWheel(lParam) is { } delta && delta != 0)
                    OnWheel(delta);
                break; // WM_INPUT must still reach DefWindowProc so the system can clean up
            case PInvoke.WM_CLOSE:
                PInvoke.DestroyWindow(hwnd);
                return (LRESULT)0;
            case PInvoke.WM_DESTROY:
                PInvoke.PostQuitMessage(0);
                return (LRESULT)0;
        }
        return PInvoke.DefWindowProc(hwnd, message, wParam, lParam);
    }

    // Every mouse move lands here too: read the fixed-size record and leave unless it carries a vertical wheel.
    private static unsafe int? ReadWheel(LPARAM lParam)
    {
        RAWINPUT input;
        var size = (uint)sizeof(RAWINPUT);
        if (PInvoke.GetRawInputData((HRAWINPUT)(nint)lParam.Value, RAW_INPUT_DATA_COMMAND_FLAGS.RID_INPUT, &input, &size,
                (uint)sizeof(RAWINPUTHEADER)) == unchecked((uint)-1)
            || input.header.dwType != (uint)RID_DEVICE_INFO_TYPE.RIM_TYPEMOUSE)
            return null;
        var mouse = input.data.mouse.Anonymous.Anonymous;
        return (mouse.usButtonFlags & PInvoke.RI_MOUSE_WHEEL) != 0 ? (short)mouse.usButtonData : null;
    }

    private void OnWheel(int delta)
    {
        // Keys first: the speaker icon lookup below can take ~70 ms, long enough for a quick Ctrl to be let go.
        var held = Held();
        if (!PInvoke.GetCursorPos(out var point) || ZoneAt(point) is not { } zone)
            return;
        try
        {
            Wheeled?.Invoke(this, new TaskbarWheelEventArgs(zone, delta, held));
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Log.Error($"Wheel handler ({zone})", e);
        }
    }

    // The window under the cursor, not the taskbar's rect: a fullscreen app or a tray overflow popup over the
    // taskbar must not count. Its tray part is the legacy TrayNotifyWnd, which still matches the drawn tray (Q6).
    private TaskbarZone? ZoneAt(System.Drawing.Point point)
    {
        var root = PInvoke.GetAncestor(PInvoke.WindowFromPoint(point), GET_ANCESTOR_FLAGS.GA_ROOT);
        if (root == HWND.Null || !IsTaskbar(root))
            return null;
        var tray = PInvoke.FindWindowEx(root, HWND.Null, "TrayNotifyWnd", null);
        if (tray == HWND.Null || !PInvoke.GetWindowRect(tray, out var rect) || !Contains(rect, point))
            return TaskbarZone.Apps;
        return _volumeIcon.Contains(root, point) ? TaskbarZone.VolumeIcon : TaskbarZone.Tray;
    }

    private static unsafe bool IsTaskbar(HWND window)
    {
        Span<char> name = stackalloc char[32];
        int length;
        fixed (char* buffer = name)
            length = PInvoke.GetClassName(window, buffer, name.Length);
        var cls = name[..length];
        return cls.SequenceEqual("Shell_TrayWnd") || cls.SequenceEqual("Shell_SecondaryTrayWnd");
    }

    internal static bool Contains(RECT rect, System.Drawing.Point point) =>
        point.X >= rect.left && point.X < rect.right && point.Y >= rect.top && point.Y < rect.bottom;

    private static HeldModifiers Held()
    {
        static bool Down(VIRTUAL_KEY key) => (PInvoke.GetAsyncKeyState((int)key) & 0x8000) != 0;
        var held = HeldModifiers.None;
        if (Down(VIRTUAL_KEY.VK_CONTROL))
            held |= HeldModifiers.Ctrl;
        if (Down(VIRTUAL_KEY.VK_SHIFT))
            held |= HeldModifiers.Shift;
        if (Down(VIRTUAL_KEY.VK_MENU))
            held |= HeldModifiers.Alt;
        if (Down(VIRTUAL_KEY.VK_LWIN) || Down(VIRTUAL_KEY.VK_RWIN))
            held |= HeldModifiers.Win;
        return held;
    }

    public void Dispose()
    {
        if (_window != HWND.Null)
        {
            PInvoke.PostMessage(_window, PInvoke.WM_CLOSE, default, default);
            _thread.Join();
            _window = HWND.Null;
        }
    }

    /// <summary>
    /// Where the tray's speaker icon is, found through UIA and kept for a second: a scroll is many notches, and
    /// icons coming and going move it. Recognized by its tooltip naming the default output; "Volume" covers an
    /// English system without one. Used on the wheel thread only.
    /// </summary>
    private sealed class VolumeIconLocator(Func<string?> outputName)
    {
        private static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(1);
        private IUIAutomation? _automation;
        private HWND _taskbar;
        private RECT? _icon;
        private long _readAt;
        private bool _loggedCost;

        public bool Contains(HWND taskbar, System.Drawing.Point point)
        {
            if (taskbar != _taskbar || Stopwatch.GetElapsedTime(_readAt) > MaxAge)
            {
                var started = Stopwatch.GetTimestamp();
                _icon = Find(taskbar);
                (_taskbar, _readAt) = (taskbar, Stopwatch.GetTimestamp());
                var took = Stopwatch.GetElapsedTime(started, _readAt);
                if (!_loggedCost || took > TimeSpan.FromMilliseconds(100))
                {
                    Log.Info($"wheel: speaker icon {(_icon is { } r ? $"at {r.left},{r.top} {r.right - r.left}x{r.bottom - r.top}" : "not found")}, took {took.TotalMilliseconds:F0} ms");
                    _loggedCost = true;
                }
            }
            return _icon is { } icon && TaskbarWheelSource.Contains(icon, point);
        }

        private RECT? Find(HWND taskbar)
        {
            try
            {
                _automation ??= (IUIAutomation)new CUIAutomation();
                var root = _automation.ElementFromHandle(taskbar);
                var buttons = root.FindAll(TreeScope.TreeScope_Descendants,
                    _automation.CreatePropertyCondition(UIA_PROPERTY_ID.UIA_ControlTypePropertyId, (int)UIA_CONTROLTYPE_ID.UIA_ButtonControlTypeId));
                var output = outputName();
                for (var i = 0; i < buttons.Length; i++)
                {
                    var button = buttons.GetElement(i);
                    if (!Read(button.CurrentClassName).StartsWith("SystemTray.", StringComparison.Ordinal))
                        continue;
                    var name = Read(button.CurrentName);
                    if ((!string.IsNullOrEmpty(output) && name.Contains(output, StringComparison.OrdinalIgnoreCase))
                        || name.StartsWith("Volume", StringComparison.OrdinalIgnoreCase))
                        return button.CurrentBoundingRectangle;
                }
                return null;
            }
            catch (COMException e)
            {
                // Explorer restarting, or the element went away mid-walk: the tray just counts as tray.
                Log.Info($"wheel: speaker icon lookup failed: 0x{e.HResult:X8}");
                _automation = null;
                return null;
            }
        }

        private static string Read(BSTR value)
        {
            try
            {
                return value.ToString() ?? "";
            }
            finally
            {
                Marshal.FreeBSTR(value);
            }
        }
    }
}
