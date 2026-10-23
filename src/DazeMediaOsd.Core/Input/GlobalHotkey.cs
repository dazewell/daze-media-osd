using System.Runtime.InteropServices;
using DazeMediaOsd.Core.Diagnostics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace DazeMediaOsd.Core.Input;

/// <summary>
/// Global shortcuts through <c>RegisterHotKey</c> (D-020), one id per chord (D-022): no hook, no admin, and
/// Windows refuses a chord cleanly when another app or the shell owns it. Runs its own message-only window
/// on a dedicated thread; <see cref="Pressed"/> is raised there.
/// </summary>
public sealed class GlobalHotkey : IDisposable
{
    private const string WindowClass = "DazeMediaOsd.Hotkey";
    private const uint SetMessage = PInvoke.WM_APP + 1;
    private const int ErrorHotkeyAlreadyRegistered = 1409;

    private readonly Thread _thread;
    private readonly WNDPROC _wndProc; // the window class holds only a native pointer: keep the delegate alive
    private HWND _window;
    private IReadOnlyDictionary<int, Hotkey> _desired = new Dictionary<int, Hotkey>(); // handed to the window thread by SendMessage, like _paused
    private bool _paused;
    private readonly HashSet<int> _registered = [];

    public GlobalHotkey()
    {
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
            Name = "Hotkey",
        };
        _thread.Start();
        ready.Wait();
        if (failure is not null)
            throw failure;
    }

    /// <summary>A registered shortcut was pressed (auto-repeat excluded); the argument is its id. Raised on the hotkey thread.</summary>
    public event EventHandler<int>? Pressed;

    /// <summary>Why the last <see cref="Set"/> didn't register a chord, by id, for Settings; ids that registered are absent.</summary>
    public IReadOnlyDictionary<int, string> Errors { get; private set; } = new Dictionary<int, string>();

    /// <summary>Replaces every shortcut with <paramref name="hotkeys"/> (id → chord) and returns <see cref="Errors"/>. Blocks until done.</summary>
    public IReadOnlyDictionary<int, string> Set(IReadOnlyDictionary<int, Hotkey> hotkeys)
    {
        _desired = hotkeys;
        return Apply();
    }

    /// <summary>
    /// While Settings records a new shortcut none may fire, or pressing one there would switch a device
    /// instead of reaching the box. <see cref="Set"/> still reports errors while paused.
    /// </summary>
    public void Pause(bool paused)
    {
        if (_paused == paused)
            return;
        _paused = paused;
        Apply();
    }

    private IReadOnlyDictionary<int, string> Apply()
    {
        if (_window != HWND.Null)
            PInvoke.SendMessage(_window, SetMessage, default, default);
        return Errors;
    }

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
            case PInvoke.WM_HOTKEY when _registered.Contains((int)wParam.Value):
                Raise((int)wParam.Value);
                return (LRESULT)0;
            case SetMessage:
                Register(hwnd, _desired, _paused);
                return (LRESULT)0;
            case PInvoke.WM_CLOSE:
                Register(hwnd, new Dictionary<int, Hotkey>(), false);
                PInvoke.DestroyWindow(hwnd);
                return (LRESULT)0;
            case PInvoke.WM_DESTROY:
                PInvoke.PostQuitMessage(0);
                return (LRESULT)0;
        }
        return PInvoke.DefWindowProc(hwnd, message, wParam, lParam);
    }

    // All at once: release every chord, then register the wanted ones. Paused: register each only to learn
    // whether Windows accepts it, then let go.
    private void Register(HWND hwnd, IReadOnlyDictionary<int, Hotkey> hotkeys, bool paused)
    {
        foreach (var id in _registered)
            PInvoke.UnregisterHotKey(hwnd, id);
        _registered.Clear();
        var errors = new Dictionary<int, string>();
        foreach (var (id, hotkey) in hotkeys)
        {
            var modifiers = (HOT_KEY_MODIFIERS)hotkey.Modifiers | HOT_KEY_MODIFIERS.MOD_NOREPEAT;
            if (PInvoke.RegisterHotKey(hwnd, id, modifiers, hotkey.Key))
            {
                if (paused)
                    PInvoke.UnregisterHotKey(hwnd, id);
                else
                    _registered.Add(id);
                continue;
            }
            var error = Marshal.GetLastPInvokeError();
            errors[id] = error == ErrorHotkeyAlreadyRegistered
                ? $"{hotkey} is already used by Windows or another app. Pick another shortcut."
                : $"Couldn't register {hotkey} (error {error}).";
            Log.Info($"hotkey {hotkey} not registered: error {error}");
        }
        Errors = errors;
        if (hotkeys.Count > errors.Count)
            Log.Info($"hotkeys registered: {string.Join(", ", hotkeys.Where(h => !errors.ContainsKey(h.Key)).Select(h => h.Value))}"
                + (paused ? " (paused while Settings records one)" : ""));
    }

    private void Raise(int id)
    {
        try
        {
            Pressed?.Invoke(this, id);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Log.Error("hotkey handler", e);
        }
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
}
