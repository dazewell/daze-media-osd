using System.Runtime.InteropServices;
using DazeMediaOsd.Core.Diagnostics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace DazeMediaOsd.Core.Input;

/// <summary>
/// One global shortcut through <c>RegisterHotKey</c> (D-020): no hook, no admin, and Windows refuses it
/// cleanly when another app or the shell owns the chord. Runs its own message-only window on a dedicated
/// thread; <see cref="Pressed"/> is raised there.
/// </summary>
public sealed class GlobalHotkey : IDisposable
{
    private const string WindowClass = "DazeMediaOsd.Hotkey";
    private const int HotkeyId = 1;
    private const uint SetMessage = PInvoke.WM_APP + 1;
    private const int ErrorHotkeyAlreadyRegistered = 1409;

    private readonly Thread _thread;
    private readonly WNDPROC _wndProc; // the window class holds only a native pointer: keep the delegate alive
    private HWND _window;
    private Hotkey? _pending; // handed to the window thread by Set's SendMessage
    private bool _registered;

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

    /// <summary>The registered shortcut was pressed (auto-repeat excluded). Raised on the hotkey thread.</summary>
    public event EventHandler? Pressed;

    /// <summary>Why the last <see cref="Set"/> didn't register, for Settings; null when it did or it is off.</summary>
    public string? Error { get; private set; }

    /// <summary>Replaces the shortcut (null: none) and returns <see cref="Error"/>. Blocks until done.</summary>
    public string? Set(Hotkey? hotkey)
    {
        if (_window == HWND.Null)
            return Error;
        _pending = hotkey;
        PInvoke.SendMessage(_window, SetMessage, default, default);
        return Error;
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
            case PInvoke.WM_HOTKEY when wParam.Value == HotkeyId:
                Raise();
                return (LRESULT)0;
            case SetMessage:
                Register(hwnd, _pending);
                return (LRESULT)0;
            case PInvoke.WM_CLOSE:
                Register(hwnd, null);
                PInvoke.DestroyWindow(hwnd);
                return (LRESULT)0;
            case PInvoke.WM_DESTROY:
                PInvoke.PostQuitMessage(0);
                return (LRESULT)0;
        }
        return PInvoke.DefWindowProc(hwnd, message, wParam, lParam);
    }

    private void Register(HWND hwnd, Hotkey? hotkey)
    {
        if (_registered)
        {
            PInvoke.UnregisterHotKey(hwnd, HotkeyId);
            _registered = false;
        }
        Error = null;
        if (hotkey is null)
            return;
        var modifiers = (HOT_KEY_MODIFIERS)hotkey.Modifiers | HOT_KEY_MODIFIERS.MOD_NOREPEAT;
        _registered = PInvoke.RegisterHotKey(hwnd, HotkeyId, modifiers, hotkey.Key);
        if (_registered)
        {
            Log.Info($"hotkey {hotkey} registered");
            return;
        }
        var error = Marshal.GetLastPInvokeError();
        Error = error == ErrorHotkeyAlreadyRegistered
            ? $"{hotkey} is already used by Windows or another app. Pick another shortcut."
            : $"Couldn't register {hotkey} (error {error}).";
        Log.Info($"hotkey {hotkey} not registered: error {error}");
    }

    private void Raise()
    {
        try
        {
            Pressed?.Invoke(this, EventArgs.Empty);
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
