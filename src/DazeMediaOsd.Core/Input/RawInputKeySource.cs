using System.Runtime.InteropServices;
using DazeMediaOsd.Core.Diagnostics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace DazeMediaOsd.Core.Input;

/// <summary>
/// Sees volume key presses passively through raw input (RIDEV_INPUTSINK): unlike a low-level hook it
/// adds no latency to anyone's input and can't block it. Needed because a key at 0 %/100 % changes no
/// volume, so the audio callbacks alone would miss it (D-006). Runs its own message-only window on a
/// dedicated thread.
/// </summary>
public sealed class RawInputKeySource : IKeySource
{
    private const string WindowClass = "DazeMediaOsd.RawInput";

    private readonly Thread _thread;
    private readonly WNDPROC _wndProc; // the window class holds only a native pointer: keep the delegate alive
    private HWND _window;

    public RawInputKeySource()
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
            Name = "RawInput",
        };
        _thread.Start();
        ready.Wait();
        if (failure is not null)
            throw failure;
    }

    public event EventHandler<VolumeKeyEventArgs>? KeyPressed;

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

        // Generic desktop / keyboard. Volume keys from HID consumer-control devices arrive here as VKs too.
        RAWINPUTDEVICE device = new()
        {
            usUsagePage = 0x01,
            usUsage = 0x06,
            dwFlags = RAWINPUTDEVICE_FLAGS.RIDEV_INPUTSINK,
            hwndTarget = _window,
        };
        if (!PInvoke.RegisterRawInputDevices([device], (uint)sizeof(RAWINPUTDEVICE)))
            throw new InvalidOperationException("RegisterRawInputDevices failed.");
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
                if (ReadVolumeKey(lParam) is { } key)
                    Raise(key);
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

    private static unsafe VolumeKey? ReadVolumeKey(LPARAM lParam)
    {
        RAWINPUT input;
        var size = (uint)sizeof(RAWINPUT);
        var read = PInvoke.GetRawInputData((HRAWINPUT)(nint)lParam.Value, RAW_INPUT_DATA_COMMAND_FLAGS.RID_INPUT,
            &input, &size, (uint)sizeof(RAWINPUTHEADER));
        if (read == unchecked((uint)-1) || input.header.dwType != (uint)RID_DEVICE_INFO_TYPE.RIM_TYPEKEYBOARD)
            return null;
        var keyboard = input.data.keyboard;
        if ((keyboard.Flags & PInvoke.RI_KEY_BREAK) != 0)
            return null;
        return (VIRTUAL_KEY)keyboard.VKey switch
        {
            VIRTUAL_KEY.VK_VOLUME_UP => VolumeKey.Up,
            VIRTUAL_KEY.VK_VOLUME_DOWN => VolumeKey.Down,
            VIRTUAL_KEY.VK_VOLUME_MUTE => VolumeKey.Mute,
            _ => null,
        };
    }

    private void Raise(VolumeKey key)
    {
        try
        {
            KeyPressed?.Invoke(this, new VolumeKeyEventArgs(key));
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Log.Error($"Volume key handler ({key})", e);
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
