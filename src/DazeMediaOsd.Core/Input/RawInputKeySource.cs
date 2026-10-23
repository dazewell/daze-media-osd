using System.Runtime.InteropServices;
using DazeMediaOsd.Core.Diagnostics;
using Windows.Win32;
using Windows.Win32.Devices.HumanInterfaceDevice;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace DazeMediaOsd.Core.Input;

/// <summary>
/// Sees volume and media key presses passively through raw input (RIDEV_INPUTSINK): unlike a low-level hook it
/// adds no latency to anyone's input and can't block it. Needed because a key at 0 %/100 % changes no
/// volume, so the audio callbacks alone would miss it (D-006); media keys change no volume at all (D-011). Runs its own message-only window on a
/// dedicated thread. Listens to two collections: keyboards (VK_VOLUME_*, VK_MEDIA_*) and Consumer Control HID
/// devices (media keys/knobs that may not be translated into keyboard input). A press seen on both
/// paths raises twice; showing the OSD is idempotent, so that is harmless.
/// </summary>
public sealed class RawInputKeySource : IKeySource
{
    private const string WindowClass = "DazeMediaOsd.RawInput";

    private readonly Thread _thread;
    private readonly WNDPROC _wndProc; // the window class holds only a native pointer: keep the delegate alive
    private readonly ConsumerKeyTracker _consumerKeys = new(); // used only on the input thread
    private readonly Dictionary<nint, byte[]> _preparsedData = []; // per HID device, input thread only
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

    public event EventHandler<MediaKeyEventArgs>? KeyPressed;

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

        ReadOnlySpan<RAWINPUTDEVICE> devices =
        [
            // Generic desktop / keyboard.
            new() { usUsagePage = 0x01, usUsage = 0x06, dwFlags = RAWINPUTDEVICE_FLAGS.RIDEV_INPUTSINK, hwndTarget = _window },
            // Consumer / Consumer Control.
            new() { usUsagePage = ConsumerKeyTracker.ConsumerPage, usUsage = 0x01, dwFlags = RAWINPUTDEVICE_FLAGS.RIDEV_INPUTSINK, hwndTarget = _window },
        ];
        if (!PInvoke.RegisterRawInputDevices(devices, (uint)sizeof(RAWINPUTDEVICE)))
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
                foreach (var key in ReadKeys(lParam))
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

    private unsafe List<MediaKey> ReadKeys(LPARAM lParam)
    {
        var handle = (HRAWINPUT)(nint)lParam.Value;
        var headerSize = (uint)sizeof(RAWINPUTHEADER);
        uint size = 0;
        // HID reports are variable-length: ask for the size first.
        if (PInvoke.GetRawInputData(handle, RAW_INPUT_DATA_COMMAND_FLAGS.RID_INPUT, null, &size, headerSize) != 0
            || size < headerSize)
            return [];
        var buffer = new byte[size];
        fixed (byte* data = buffer)
        {
            if (PInvoke.GetRawInputData(handle, RAW_INPUT_DATA_COMMAND_FLAGS.RID_INPUT, data, &size, headerSize) == unchecked((uint)-1))
                return [];
            var input = (RAWINPUT*)data;
            return input->header.dwType switch
            {
                (uint)RID_DEVICE_INFO_TYPE.RIM_TYPEKEYBOARD => KeyboardKey(input->data.keyboard) is { } key ? [key] : [],
                (uint)RID_DEVICE_INFO_TYPE.RIM_TYPEHID => ConsumerKeys(input),
                _ => [],
            };
        }
    }

    private static MediaKey? KeyboardKey(RAWKEYBOARD keyboard)
    {
        if ((keyboard.Flags & PInvoke.RI_KEY_BREAK) != 0)
            return null;
        return (VIRTUAL_KEY)keyboard.VKey switch
        {
            VIRTUAL_KEY.VK_VOLUME_UP => MediaKey.VolumeUp,
            VIRTUAL_KEY.VK_VOLUME_DOWN => MediaKey.VolumeDown,
            VIRTUAL_KEY.VK_VOLUME_MUTE => MediaKey.Mute,
            VIRTUAL_KEY.VK_MEDIA_PLAY_PAUSE => MediaKey.PlayPause,
            VIRTUAL_KEY.VK_MEDIA_NEXT_TRACK => MediaKey.NextTrack,
            VIRTUAL_KEY.VK_MEDIA_PREV_TRACK => MediaKey.PreviousTrack,
            _ => null,
        };
    }

    private unsafe List<MediaKey> ConsumerKeys(RAWINPUT* input)
    {
        var device = (nint)input->header.hDevice.Value;
        if (PreparsedData(device) is not { } preparsed)
            return [];
        var hid = &input->data.hid;
        var reports = (byte*)&hid->bRawData;
        var keys = new List<MediaKey>();
        fixed (byte* preparsedPtr = preparsed)
        {
            var ppd = new PHIDP_PREPARSED_DATA((nint)preparsedPtr);
            const int MaxUsages = 64;
            var usages = stackalloc USAGE_AND_PAGE[MaxUsages];
            for (var i = 0; i < hid->dwCount; i++)
            {
                uint count = MaxUsages;
                var report = new PSTR(reports + (i * hid->dwSizeHid));
                if (PInvoke.HidP_GetUsagesEx(HIDP_REPORT_TYPE.HidP_Input, 0, usages, &count, ppd, report, hid->dwSizeHid)
                    != NTSTATUS.HIDP_STATUS_SUCCESS)
                    continue;
                var held = new (ushort, ushort)[count];
                for (var u = 0; u < count; u++)
                    held[u] = (usages[u].UsagePage, usages[u].Usage);
                foreach (var key in _consumerKeys.Update(device, held))
                {
                    Log.Info($"consumer-control key {key} from HID device {device:X}");
                    keys.Add(key);
                }
            }
        }
        return keys;
    }

    private unsafe byte[]? PreparsedData(nint device)
    {
        if (_preparsedData.TryGetValue(device, out var cached))
            return cached;
        uint size = 0;
        var handle = new HANDLE(device);
        // Documented to return 0 for a NULL buffer (it does on 26300); only -1 is failure, the size is in `size`.
        if (PInvoke.GetRawInputDeviceInfo(handle, RAW_INPUT_DEVICE_INFO_COMMAND.RIDI_PREPARSEDDATA, null, &size) == unchecked((uint)-1)
            || size == 0)
            return null;
        var data = new byte[size];
        fixed (byte* p = data)
        {
            if (PInvoke.GetRawInputDeviceInfo(handle, RAW_INPUT_DEVICE_INFO_COMMAND.RIDI_PREPARSEDDATA, p, &size) == unchecked((uint)-1))
                return null;
        }
        return _preparsedData[device] = data;
    }

    private void Raise(MediaKey key)
    {
        try
        {
            KeyPressed?.Invoke(this, new MediaKeyEventArgs(key));
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Log.Error($"Key handler ({key})", e);
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
