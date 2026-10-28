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
/// window, so the stream of mouse-move messages never delays the key source. Nothing on that thread waits: the
/// speaker icon's place comes from a cache that the thread pool refreshes, so queued notches keep their keys and
/// cursor position.
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
        _thread.Start();
        ready.Wait();
        if (failure is not null)
            throw failure;
        // Find the speaker icon now, so the first notch over the tray isn't skipped while it is looked up.
        var primary = PInvoke.FindWindow("Shell_TrayWnd", null);
        if (primary != HWND.Null)
            _volumeIcon.Refresh(primary);
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
        // Unknown (not read yet, or UIA failed): skip the notch. Counting it as tray could double the speaker
        // icon's own step.
        return _volumeIcon.Hit(root, point) switch
        {
            IconHit.Inside => TaskbarZone.VolumeIcon,
            IconHit.Outside => TaskbarZone.Tray,
            _ => null,
        };
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

    private enum IconHit
    {
        /// <summary>Not read yet for this taskbar, or the last read failed.</summary>
        Unknown,
        Inside,
        Outside,
    }

    /// <summary>
    /// Where the tray's speaker icon is, found through UIA (~70 ms) on the thread pool and refreshed once a second
    /// old: icons coming and going move it. The wheel thread only reads the last result, so a lookup never holds
    /// up notches queued behind it. Recognized by its tooltip naming the default output; "Volume" covers an
    /// English system without one.
    /// </summary>
    private sealed class VolumeIconLocator(Func<string?> outputName)
    {
        private static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(1);
        private readonly System.Collections.Concurrent.ConcurrentDictionary<nint, Reading> _readings = new();
        private IUIAutomation? _automation; // used by one refresh at a time
        private int _refreshing;
        private bool _loggedCost;

        /// <param name="Found">False when UIA failed: the icon's place is unknown.</param>
        private sealed record Reading(bool Found, RECT? Icon, long ReadAt);

        public IconHit Hit(HWND taskbar, System.Drawing.Point point)
        {
            var reading = _readings.GetValueOrDefault(taskbar);
            if (reading is null || Stopwatch.GetElapsedTime(reading.ReadAt) > MaxAge)
                Refresh(taskbar); // meanwhile the last reading still answers: the icon rarely moves
            return reading switch
            {
                null or { Found: false } => IconHit.Unknown,
                { Icon: { } icon } when Contains(icon, point) => IconHit.Inside,
                _ => IconHit.Outside,
            };
        }

        public void Refresh(HWND taskbar)
        {
            if (Interlocked.Exchange(ref _refreshing, 1) == 1)
                return;
            ThreadPool.UnsafeQueueUserWorkItem(_ =>
            {
                try
                {
                    var started = Stopwatch.GetTimestamp();
                    var (found, icon) = Find(taskbar);
                    var reading = new Reading(found, icon, Stopwatch.GetTimestamp());
                    _readings[taskbar] = reading;
                    var took = Stopwatch.GetElapsedTime(started, reading.ReadAt);
                    if (!_loggedCost || took > TimeSpan.FromMilliseconds(100))
                    {
                        Log.Info($"wheel: speaker icon {(icon is { } r ? $"at {r.left},{r.top} {r.right - r.left}x{r.bottom - r.top}" : found ? "not found" : "lookup failed")}, took {took.TotalMilliseconds:F0} ms");
                        _loggedCost = true;
                    }
                }
                finally
                {
                    Volatile.Write(ref _refreshing, 0);
                }
            }, null);
        }

        private (bool Found, RECT? Icon) Find(HWND taskbar)
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
                        return (true, button.CurrentBoundingRectangle);
                }
                return (true, null); // no speaker icon on this taskbar (a secondary one, or hidden)
            }
            catch (COMException e)
            {
                // Explorer restarting, or the element went away mid-walk: unknown until the next refresh.
                Log.Info($"wheel: speaker icon lookup failed: 0x{e.HResult:X8}");
                _automation = null;
                return (false, null);
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
