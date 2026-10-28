// Plan D spike probe (Q6, Q7; D-035): can we tell the taskbar's tray from its task buttons when the wheel
// turns, and does Windows itself already act on a wheel over the taskbar? Not product code.
//
//   dotnet run --project spikes/TaskbarWheelProbe -- dump    child HWNDs + UIA tree of every taskbar
//   dotnet run --project spikes/TaskbarWheelProbe -- watch   log each real wheel notch for 60 s: zone guesses, modifiers,
//                                                           UIA hit + its cost, native volume change
//   dotnet run --project spikes/TaskbarWheelProbe -- auto    move the cursor over sampled taskbar points and scroll one
//                                                           notch (none / Ctrl / Shift); log native volume changes,
//                                                           restore the volume after each, the cursor at the end
//
// Output goes to the console and ./probe-out/<mode>.log.
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using System.Windows.Forms;

static class Program
{
    static readonly Stopwatch Clock = Stopwatch.StartNew();
    static StreamWriter? _log;

    [STAThread]
    static void Main(string[] args)
    {
        var mode = args.FirstOrDefault() ?? "watch";
        Directory.CreateDirectory("probe-out");
        _log = new StreamWriter(Path.Combine("probe-out", $"{mode}.log"), false, Encoding.UTF8) { AutoFlush = true };
        Native.SetProcessDpiAwarenessContext(-4); // per-monitor v2: physical pixels everywhere
        Log($"mode={mode} build={Environment.OSVersion.Version}");
        switch (mode)
        {
            case "dump": Dump(); break;
            case "watch": Watch(TimeSpan.FromSeconds(60), null); break;
            case "auto": Watch(null, Auto); break;
            default: Log("unknown mode"); break;
        }
    }

    internal static void Log(string line)
    {
        var text = $"{Clock.ElapsedMilliseconds,7} {line}";
        Console.WriteLine(text);
        _log?.WriteLine(text);
    }

    static int _rawMouseEvents;
    internal static void CountRaw() => Interlocked.Increment(ref _rawMouseEvents);

    // ---- dump -------------------------------------------------------------------------------------------

    static void Dump()
    {
        foreach (var bar in Taskbars())
        {
            Log($"== {Native.ClassOf(bar)} {bar:X} rect={Native.RectOf(bar)}");
            Native.EnumChildWindows(bar, (child, _) =>
            {
                Log($"   hwnd {Native.ClassOf(child),-45} {Native.RectOf(child)} visible={Native.IsWindowVisible(child)} parent={Native.ClassOf(Native.GetParent(child))}");
                return true;
            }, 0);
            var sw = Stopwatch.StartNew();
            var root = AutomationElement.FromHandle(bar);
            DumpUia(root, 0);
            Log($"   uia walk {sw.ElapsedMilliseconds} ms");
        }
    }

    static void DumpUia(AutomationElement element, int depth)
    {
        if (depth > 10)
            return;
        Log($"   {new string(' ', depth * 2)}uia {Describe(element)}");
        var walker = TreeWalker.RawViewWalker;
        for (var child = walker.GetFirstChild(element); child is not null; child = walker.GetNextSibling(child))
            DumpUia(child, depth + 1);
    }

    static string Describe(AutomationElement e)
    {
        try
        {
            var c = e.Current;
            return $"{c.ControlType.ProgrammaticName.Replace("ControlType.", "")} class='{c.ClassName}' id='{c.AutomationId}' name='{c.Name}' rect={c.BoundingRectangle}";
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or COMException)
        {
            return $"<gone: {ex.GetType().Name}>";
        }
    }

    static List<nint> Taskbars()
    {
        var bars = new List<nint>();
        Native.EnumWindows((hwnd, _) =>
        {
            var cls = Native.ClassOf(hwnd);
            if (cls is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
                bars.Add(hwnd);
            return true;
        }, 0);
        return bars;
    }

    // ---- watch ------------------------------------------------------------------------------------------

    static float _polledVolume = -1;
    static long _polledAt;

    static void Watch(TimeSpan? duration, Action? worker)
    {
        using var volume = new Volume();
        using var sink = new WheelSink(OnWheel);
        var poll = new System.Windows.Forms.Timer { Interval = 20 };
        poll.Tick += (_, _) => { _polledVolume = volume.Get(); _polledAt = Clock.ElapsedMilliseconds; };
        poll.Start();
        if (duration is { } d)
        {
            Log($"=== scroll over the taskbar (tasks, tray icons, speaker icon, clock), with and without Ctrl/Shift/Alt, for {d.TotalSeconds} s");
            var stop = new System.Windows.Forms.Timer { Interval = (int)d.TotalMilliseconds };
            stop.Tick += (_, _) => Application.ExitThread();
            stop.Start();
        }
        if (worker is not null)
            new Thread(() => { try { worker(); } catch (Exception e) { Log($"worker failed: {e}"); Environment.Exit(1); } }) { IsBackground = true }
                .Start();
        Application.Run();
    }

    static void OnWheel(short delta, bool horizontal, nint device)
    {
        Native.GetCursorPos(out var pt);
        var volumeBefore = _polledVolume;
        var sb = new StringBuilder();
        sb.Append($"WHEEL {(horizontal ? "h" : "v")}{delta:+#;-#} dev={device:X} at=({pt.X},{pt.Y}) mods={Modifiers()}");

        // Candidate A: legacy HWNDs.
        var hit = Native.WindowFromPoint(pt);
        var root = Native.GetAncestor(hit, 2 /* GA_ROOT */);
        var rootClass = Native.ClassOf(root);
        sb.Append($"\n        hwnd hit={Native.ClassOf(hit)} root={rootClass} rootCloaked={Native.Cloaked(root)} rootOwner={Native.OwnerOf(root)} rootRect={Native.RectOf(root)}");
        sb.Append($" => zoneRect={ZoneByRect(pt)}");
        string zoneA = "none";
        if (rootClass is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
        {
            var containing = new List<string>();
            Native.EnumChildWindows(root, (child, _) =>
            {
                if (Native.IsWindowVisible(child) && Native.RectOf(child).Contains(pt))
                    containing.Add(Native.ClassOf(child));
                return true;
            }, 0);
            sb.Append($" containing=[{string.Join(", ", containing)}]");
            zoneA = containing.Contains("TrayNotifyWnd") ? "tray" : "tasks";
        }
        sb.Append($" => zoneA={zoneA}");

        // Candidate B: UIA ancestors of the element under the cursor.
        var sw = Stopwatch.StartNew();
        try
        {
            var element = AutomationElement.FromPoint(new System.Windows.Point(pt.X, pt.Y));
            var chain = new List<string>();
            var walker = TreeWalker.RawViewWalker;
            for (var e = element; e is not null && chain.Count < 8; e = walker.GetParent(e))
            {
                if (e == AutomationElement.RootElement)
                    break;
                chain.Add(Describe(e));
            }
            sb.Append($"\n        uia {sw.ElapsedMilliseconds} ms:");
            foreach (var link in chain)
                sb.Append($"\n          {link}");
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or COMException or InvalidOperationException)
        {
            sb.Append($"\n        uia failed after {sw.ElapsedMilliseconds} ms: {ex.GetType().Name}");
        }
        Log(sb.ToString());

        // Q7: native volume reaction, judged against the poll 400 ms later.
        var notchAt = Clock.ElapsedMilliseconds;
        var check = new System.Windows.Forms.Timer { Interval = 400 };
        check.Tick += (_, _) =>
        {
            check.Dispose();
            var after = _polledVolume;
            var changed = Math.Abs(after - volumeBefore) > 0.001f;
            Log($"        volume {volumeBefore:P0} -> {after:P0} {(changed ? "NATIVE CHANGE" : "unchanged")} (notch at {notchAt})");
        };
        check.Start();
    }

    // Candidate A': ignore WindowFromPoint (cloaked shell windows sit over the taskbar), test the rects directly.
    static string ZoneByRect(Native.POINT pt)
    {
        foreach (var bar in Taskbars())
        {
            if (!Native.RectOf(bar).Contains(pt))
                continue;
            var tray = Native.FindWindowEx(bar, 0, "TrayNotifyWnd", null);
            var where = tray != 0 && Native.RectOf(tray).Contains(pt) ? "tray" : "tasks";
            return $"{where}@{Native.ClassOf(bar)}";
        }
        return "none";
    }

    static string Modifiers()
    {
        var mods = new List<string>();
        if (Native.Down(0x11)) mods.Add("Ctrl");
        if (Native.Down(0x10)) mods.Add("Shift");
        if (Native.Down(0x12)) mods.Add("Alt");
        if (Native.Down(0x5B) || Native.Down(0x5C)) mods.Add("Win");
        return mods.Count == 0 ? "none" : string.Join("+", mods);
    }

    // ---- auto -------------------------------------------------------------------------------------------

    static void Auto()
    {
        using var volume = new Volume();
        Native.GetCursorPos(out var home);
        var targets = new List<(string what, Native.POINT at)>();
        foreach (var bar in Taskbars())
        {
            var barRect = Native.RectOf(bar);
            var seen = new HashSet<(int, int)>();
            void Collect(AutomationElement e, int depth)
            {
                if (depth > 10) return;
                try
                {
                    var c = e.Current;
                    var r = c.BoundingRectangle;
                    if (depth > 0 && !r.IsEmpty && r.Width > 0 && r.Width <= 300 && r.Height <= barRect.Height + 2
                        && c.ControlType == ControlType.Button)
                    {
                        var p = new Native.POINT { X = (int)(r.X + r.Width / 2), Y = (int)(r.Y + r.Height / 2) };
                        if (barRect.Contains(p) && seen.Add((p.X, p.Y)))
                            targets.Add(($"{Native.ClassOf(bar)} {c.ControlType.ProgrammaticName.Replace("ControlType.", "")} id='{c.AutomationId}' name='{c.Name}'", p));
                    }
                }
                catch (Exception ex) when (ex is ElementNotAvailableException or COMException) { return; }
                var walker = TreeWalker.RawViewWalker;
                for (var child = walker.GetFirstChild(e); child is not null; child = walker.GetNextSibling(child))
                    Collect(child, depth + 1);
            }
            Collect(AutomationElement.FromHandle(bar), 0);
            // Empty taskbar space, a few px in from the left-of-tray gap is unknown: also probe 2 px in from each end.
            targets.Add(($"{Native.ClassOf(bar)} left edge", new Native.POINT { X = barRect.Left + 2, Y = (barRect.Top + barRect.Bottom) / 2 }));
            targets.Add(($"{Native.ClassOf(bar)} right edge", new Native.POINT { X = barRect.Right - 3, Y = (barRect.Top + barRect.Bottom) / 2 }));
        }
        Log($"=== {targets.Count} targets; cursor will move. Volume restored after each notch.");
        string[] modifiers = ["none", "Ctrl", "Shift"];
        foreach (var (what, at) in targets)
        {
            Log($"--- target {what} at ({at.X},{at.Y})");
            foreach (var mod in modifiers)
            {
                var original = volume.Get();
                // Stay below 100 % so an up-notch can show.
                if (original > 0.95f) volume.Set(0.5f);
                var start = volume.Get();
                Native.SetCursorPos(at.X, at.Y);
                Thread.Sleep(250);
                ushort vk = mod switch { "Ctrl" => 0x11, "Shift" => 0x10, _ => 0 };
                if (vk != 0) Native.Key(vk, down: true);
                Log($"    send notch +120 mods={mod}");
                var sent = Native.Wheel(120);
                if (vk != 0) Native.Key(vk, down: false);
                Thread.Sleep(450);
                var after = volume.Get();
                Log($"    auto result mods={mod} sent={sent} rawSoFar={_rawMouseEvents}: volume {start:P0} -> {after:P0} {(Math.Abs(after - start) > 0.001f ? "NATIVE CHANGE" : "unchanged")}");
                volume.Set(original);
            }
        }
        Native.SetCursorPos(home.X, home.Y);
        Thread.Sleep(600); // let the last watch line land
        Log("=== done");
        Environment.Exit(0);
    }
}

/// <summary>Raw mouse input sink on a message-only window, like the product's RawInputKeySource (D-035).</summary>
sealed class WheelSink : NativeWindow, IDisposable
{
    const int WM_INPUT = 0x00FF;
    const ushort RI_MOUSE_WHEEL = 0x0400, RI_MOUSE_HWHEEL = 0x0800;
    readonly Action<short, bool, nint> _onWheel;

    public WheelSink(Action<short, bool, nint> onWheel)
    {
        _onWheel = onWheel;
        CreateHandle(new CreateParams { Parent = -3 /* HWND_MESSAGE */ });
        var device = new Native.RAWINPUTDEVICE { usUsagePage = 0x01, usUsage = 0x02, dwFlags = 0x100 /* RIDEV_INPUTSINK */, hwndTarget = Handle };
        if (!Native.RegisterRawInputDevices([device], 1, (uint)Marshal.SizeOf<Native.RAWINPUTDEVICE>()))
            throw new InvalidOperationException($"RegisterRawInputDevices failed: {Marshal.GetLastWin32Error()}");
        Program.Log("raw mouse input registered");
    }

    protected override unsafe void WndProc(ref Message m)
    {
        if (m.Msg == WM_INPUT)
        {
            const int headerSize = 24; // RAWINPUTHEADER on x64
            uint size = 0;
            Native.GetRawInputData(m.LParam, 0x10000003 /* RID_INPUT */, null, ref size, headerSize);
            if (size >= headerSize + 24)
            {
                var buffer = stackalloc byte[(int)size];
                if (Native.GetRawInputData(m.LParam, 0x10000003, buffer, ref size, headerSize) == size
                    && *(uint*)buffer == 0 /* RIM_TYPEMOUSE */)
                {
                    Program.CountRaw();
                    var device = *(nint*)(buffer + 8);
                    var flags = *(ushort*)(buffer + headerSize + 4);
                    var data = *(short*)(buffer + headerSize + 6);
                    if ((flags & RI_MOUSE_WHEEL) != 0) _onWheel(data, false, device);
                    if ((flags & RI_MOUSE_HWHEEL) != 0) _onWheel(data, true, device);
                }
            }
        }
        base.WndProc(ref m);
    }

    public void Dispose() => DestroyHandle();
}

/// <summary>Default render endpoint master volume (Core Audio, hand-rolled COM).</summary>
sealed class Volume : IDisposable
{
    readonly IAudioEndpointVolume _endpoint;

    public Volume()
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCo();
        Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(0 /* eRender */, 0 /* eConsole */, out var device));
        var iid = typeof(IAudioEndpointVolume).GUID;
        Marshal.ThrowExceptionForHR(device.Activate(ref iid, 1 /* CLSCTX_INPROC_SERVER */, 0, out var obj));
        _endpoint = (IAudioEndpointVolume)obj;
    }

    public float Get() { _endpoint.GetMasterVolumeLevelScalar(out var v); return v; }

    public void Set(float v) { var ctx = Guid.Empty; _endpoint.SetMasterVolumeLevelScalar(v, ref ctx); }

    public void Dispose() => Marshal.FinalReleaseComObject(_endpoint);

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumeratorCo { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(int dataFlow, int stateMask, out nint devices);
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        int Activate(ref Guid iid, int clsCtx, nint activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolume
    {
        int RegisterControlChangeNotify(nint notify);
        int UnregisterControlChangeNotify(nint notify);
        int GetChannelCount(out uint count);
        int SetMasterVolumeLevel(float levelDb, ref Guid context);
        int SetMasterVolumeLevelScalar(float level, ref Guid context);
        int GetMasterVolumeLevel(out float levelDb);
        int GetMasterVolumeLevelScalar(out float level);
    }
}

static unsafe class Native
{
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public int Height => Bottom - Top;
        public bool Contains(POINT p) => p.X >= Left && p.X < Right && p.Y >= Top && p.Y < Bottom;
        public override string ToString() => $"[{Left},{Top} {Right - Left}x{Bottom - Top}]";
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUTDEVICE { public ushort usUsagePage, usUsage; public uint dwFlags; public nint hwndTarget; }

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    struct INPUT
    {
        [FieldOffset(0)] public uint type;
        [FieldOffset(8)] public int dx;
        [FieldOffset(12)] public int dy;
        [FieldOffset(16)] public uint mouseData;
        [FieldOffset(20)] public uint mouseFlags;
        [FieldOffset(8)] public ushort wVk;
        [FieldOffset(12)] public uint keyFlags;
    }

    public delegate bool EnumProc(nint hwnd, nint lParam);

    [DllImport("user32")] public static extern bool SetProcessDpiAwarenessContext(nint value);
    [DllImport("user32")] public static extern bool EnumWindows(EnumProc proc, nint lParam);
    [DllImport("user32")] public static extern bool EnumChildWindows(nint parent, EnumProc proc, nint lParam);
    [DllImport("user32", CharSet = CharSet.Unicode)] static extern int GetClassName(nint hwnd, StringBuilder name, int max);
    [DllImport("user32")] static extern bool GetWindowRect(nint hwnd, out RECT rect);
    [DllImport("user32")] public static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32")] public static extern nint GetParent(nint hwnd);
    [DllImport("user32")] public static extern nint GetAncestor(nint hwnd, uint flags);
    [DllImport("user32")] public static extern nint WindowFromPoint(POINT p);
    [DllImport("user32", CharSet = CharSet.Unicode)] public static extern nint FindWindowEx(nint parent, nint after, string? cls, string? title);
    [DllImport("dwmapi")] static extern int DwmGetWindowAttribute(nint hwnd, int attr, out int value, int size);
    [DllImport("user32")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32")] static extern short GetAsyncKeyState(int vk);
    [DllImport("user32", SetLastError = true)] public static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devices, uint count, uint size);
    [DllImport("user32")] public static extern uint GetRawInputData(nint handle, uint command, void* data, ref uint size, uint headerSize);
    [DllImport("user32", SetLastError = true)] static extern uint SendInput(uint count, INPUT* inputs, int size);

    public static string ClassOf(nint hwnd)
    {
        if (hwnd == 0) return "<null>";
        var sb = new StringBuilder(256);
        GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static RECT RectOf(nint hwnd) { GetWindowRect(hwnd, out var r); return r; }

    public static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    public static string Wheel(int delta)
    {
        var input = new INPUT { type = 0 /* MOUSE */, mouseData = unchecked((uint)delta), mouseFlags = 0x0800 /* WHEEL */ };
        return SendInput(1, &input, sizeof(INPUT)) == 1 ? "ok" : $"FAILED({Marshal.GetLastPInvokeError()})";
    }

    public static string OwnerOf(nint hwnd)
    {
        GetWindowThreadProcessId(hwnd, out var pid);
        try { return Process.GetProcessById((int)pid).ProcessName; } catch (ArgumentException) { return $"pid {pid}"; }
    }

    [DllImport("user32")] static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);

    public static bool Cloaked(nint hwnd) => DwmGetWindowAttribute(hwnd, 14 /* DWMWA_CLOAKED */, out var c, 4) == 0 && c != 0;

    public static void Key(ushort vk, bool down)
    {
        var input = new INPUT { type = 1 /* KEYBOARD */, wVk = vk, keyFlags = down ? 0u : 2u /* KEYUP */ };
        SendInput(1, &input, sizeof(INPUT));
    }
}
