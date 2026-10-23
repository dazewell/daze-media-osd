// Q1 spike probe: finds the native Win11 volume flyout host and tests per-show suppression.
// Not product code — re-run it after a Windows update to re-check the window classes (D-002, D-005).
//
//   dotnet run --project spikes/FlyoutProbe -- watch      log every top-level window event around a mute-key press
//   dotnet run --project spikes/FlyoutProbe -- suppress   hide the flyout host each time it is shown
//   dotnet run --project spikes/FlyoutProbe -- manual     like suppress, but no synthetic keys: press real ones for 30 s
//
// watch/suppress press Mute twice per round (net zero), take screenshots into ./probe-out and print a timeline.
using System.Diagnostics;
using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

static class Program
{
    const uint EVENT_OBJECT_CREATE = 0x8000, EVENT_OBJECT_DESTROY = 0x8001, EVENT_OBJECT_SHOW = 0x8002,
        EVENT_OBJECT_HIDE = 0x8003, EVENT_OBJECT_CLOAKED = 0x8017, EVENT_OBJECT_UNCLOAKED = 0x8018;
    const uint WINEVENT_OUTOFCONTEXT = 0, WINEVENT_SKIPOWNPROCESS = 2;
    const string HostClass = "XamlExplorerHostIslandWindow";

    static readonly Stopwatch Clock = Stopwatch.StartNew();
    static readonly WinEventProc Proc = OnWinEvent; // keep the delegate alive for the hook's lifetime
    static readonly int OwnPid = Environment.ProcessId;
    static string _mode = "watch";
    static long _lastKeyMs = -1;
    static readonly string OutDir = Path.Combine(Environment.CurrentDirectory, "probe-out");

    [STAThread]
    static void Main(string[] args)
    {
        _mode = args.FirstOrDefault() ?? "watch";
        Directory.CreateDirectory(OutDir);
        SetProcessDpiAwarenessContext(-4); // per-monitor v2: physical pixels in rects and screenshots
        Log($"mode={_mode} build={Environment.OSVersion.Version}");
        DumpHosts("before");

        var hook = SetWinEventHook(EVENT_OBJECT_CREATE, EVENT_OBJECT_UNCLOAKED, 0, Proc, 0, 0,
            WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        Log($"hook={hook}");

        var steps = _mode == "manual"
            ? new (int at, Action act)[]
            {
                (10,    () => Log("=== press real volume keys now; suppressing for 30 s")),
                (30000, () => { DumpHosts("end"); Application.Exit(); }),
            }
            : new (int at, Action act)[]
        {
            (800,  () => PressMute("press#1")),
            (1100, () => PressMute("press#2 (restores mute state)")),
            (1600, () => Shot("round1")),
            (4500, () => DumpHosts("after-round1")),
            // Round 2 starts after the native timeout: does the host get SHOW again?
            (6000, () => PressMute("press#3")),
            (6300, () => PressMute("press#4 (restores mute state)")),
            (6800, () => Shot("round2")),
            (9500, () => { DumpHosts("end"); Application.Exit(); }),
        };
        foreach (var (at, act) in steps)
        {
            var t = new System.Windows.Forms.Timer { Interval = at };
            t.Tick += (_, _) => { t.Stop(); act(); };
            t.Start();
        }
        Application.Run();
        UnhookWinEvent(hook);
    }

    static void OnWinEvent(nint hook, uint ev, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != 0 || idChild != 0 || hwnd == 0) return; // OBJID_WINDOW only
        if (GetAncestor(hwnd, 2 /*GA_ROOT*/) != hwnd) return;   // top-level windows only
        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == OwnPid) return;
        var cls = ClassOf(hwnd);
        // Outside the key-press windows only log the class we care about, to keep the output readable.
        var nearKey = _lastKeyMs >= 0 && Clock.ElapsedMilliseconds - _lastKeyMs < 2500;
        if (!nearKey && cls != HostClass) return;

        Log($"{EvName(ev),-9} {Describe(hwnd)}");

        if (_mode is "suppress" or "manual" && ev == EVENT_OBJECT_SHOW && IsVolumeHost(hwnd))
        {
            ShowWindowAsync(hwnd, 0 /*SW_HIDE*/);
            Log($"   -> SW_HIDE sent ({Clock.ElapsedMilliseconds - _lastKeyMs} ms after key)");
        }
    }

    static bool IsVolumeHost(nint hwnd)
    {
        if (ClassOf(hwnd) != HostClass) return false;
        GetWindowThreadProcessId(hwnd, out var pid);
        return ProcName(pid) == "explorer" && Band(hwnd) == 18 /*ZBID_ABOVELOCK_UX*/;
    }

    static void DumpHosts(string label)
    {
        Log($"--- hosts ({label}) ---");
        nint h = 0;
        while ((h = FindWindowEx(0, h, HostClass, null)) != 0)
        {
            Log($"   {Describe(h)}");
            nint c = 0;
            while ((c = FindWindowEx(h, c, null, null)) != 0)
                Log($"      child {c:X} class={ClassOf(c)} title=\"{TitleOf(c)}\" visible={IsWindowVisible(c)}");
        }
    }

    static string Describe(nint hwnd)
    {
        GetWindowThreadProcessId(hwnd, out var pid);
        GetWindowRect(hwnd, out var r);
        DwmGetWindowAttribute(hwnd, 14 /*DWMWA_CLOAKED*/, out int cloaked, 4);
        return $"{hwnd:X} proc={ProcName(pid)} class={ClassOf(hwnd)} title=\"{TitleOf(hwnd)}\" band={Band(hwnd)} " +
               $"visible={IsWindowVisible(hwnd)} cloaked={cloaked} rect=({r.L},{r.T},{r.R},{r.B}) " +
               $"exstyle={GetWindowLongPtr(hwnd, -20):X}";
    }

    static void PressMute(string label)
    {
        var inputs = new INPUT[2];
        inputs[0].type = 1; inputs[0].ki.wVk = 0xAD; // VK_VOLUME_MUTE
        inputs[1].type = 1; inputs[1].ki.wVk = 0xAD; inputs[1].ki.dwFlags = 2; // KEYEVENTF_KEYUP
        var sent = SendInput(2, inputs, Marshal.SizeOf<INPUT>());
        _lastKeyMs = Clock.ElapsedMilliseconds;
        Log($"=== {label} (SendInput={sent})");
    }

    static void Shot(string name)
    {
        var b = Screen.PrimaryScreen!.Bounds;
        using var full = new Bitmap(b.Width, b.Height);
        using (var g = Graphics.FromImage(full)) g.CopyFromScreen(b.Location, Point.Empty, b.Size);
        // Bottom-centre crop: where the Win11 volume flyout appears above the taskbar.
        var crop = new Rectangle(b.Width / 2 - 400, b.Height - 350, 800, 350);
        using var part = full.Clone(crop, full.PixelFormat);
        var path = Path.Combine(OutDir, $"{_mode}-{name}.png");
        part.Save(path, ImageFormat.Png);
        Log($"shot {path}");
    }

    static void Log(string s) => Console.WriteLine($"{Clock.ElapsedMilliseconds,6} ms  {s}");
    static string EvName(uint e) => e switch
    {
        EVENT_OBJECT_CREATE => "CREATE", EVENT_OBJECT_DESTROY => "DESTROY", EVENT_OBJECT_SHOW => "SHOW",
        EVENT_OBJECT_HIDE => "HIDE", EVENT_OBJECT_CLOAKED => "CLOAKED", EVENT_OBJECT_UNCLOAKED => "UNCLOAKED",
        _ => $"0x{e:X}",
    };
    static string ClassOf(nint h) { var sb = new StringBuilder(256); GetClassName(h, sb, 256); return sb.ToString(); }
    static string TitleOf(nint h) { var sb = new StringBuilder(256); GetWindowText(h, sb, 256); return sb.ToString(); }
    static string ProcName(int pid) { try { return Process.GetProcessById(pid).ProcessName; } catch { return "?"; } }
    static uint Band(nint h) => GetWindowBand(h, out var band) ? band : 0;

    delegate void WinEventProc(nint hook, uint ev, nint hwnd, int idObject, int idChild, uint thread, uint time);
    [DllImport("user32")] static extern nint SetWinEventHook(uint min, uint max, nint mod, WinEventProc proc, int pid, int tid, uint flags);
    [DllImport("user32")] static extern bool UnhookWinEvent(nint hook);
    [DllImport("user32")] static extern nint GetAncestor(nint h, uint flags);
    [DllImport("user32")] static extern int GetWindowThreadProcessId(nint h, out int pid);
    [DllImport("user32", CharSet = CharSet.Unicode)] static extern int GetClassName(nint h, StringBuilder sb, int max);
    [DllImport("user32", CharSet = CharSet.Unicode)] static extern int GetWindowText(nint h, StringBuilder sb, int max);
    [DllImport("user32", CharSet = CharSet.Unicode)] static extern nint FindWindowEx(nint parent, nint after, string? cls, string? title);
    [DllImport("user32")] static extern bool IsWindowVisible(nint h);
    [DllImport("user32")] static extern bool GetWindowRect(nint h, out RECT r);
    [DllImport("user32")] static extern nint GetWindowLongPtr(nint h, int idx);
    [DllImport("user32")] static extern bool ShowWindowAsync(nint h, int cmd);
    [DllImport("user32")] static extern bool GetWindowBand(nint h, out uint band); // undocumented
    [DllImport("user32")] static extern bool SetProcessDpiAwarenessContext(nint ctx);
    [DllImport("user32")] static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("dwmapi")] static extern int DwmGetWindowAttribute(nint h, int attr, out int value, int size);

    struct RECT { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)]
    struct INPUT { public uint type; public KEYBDINPUT ki; long _pad; } // x64: 40 bytes, union padded to MOUSEINPUT size
    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public nint dwExtraInfo; }
}
