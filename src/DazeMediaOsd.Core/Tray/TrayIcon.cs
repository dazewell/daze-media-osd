using System.Runtime.InteropServices;
using DazeMediaOsd.Core.Audio;
using DazeMediaOsd.Core.Diagnostics;
using DazeMediaOsd.Core.Interop;
using Microsoft.Win32;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace DazeMediaOsd.Core.Tray;

/// <summary>
/// Notification-area icon with a native context menu, via <c>Shell_NotifyIcon</c> (D-009). Runs its own
/// hidden top-level window on a dedicated thread: a message-only window would miss the TaskbarCreated
/// broadcast (explorer restart) and WM_SETTINGCHANGE (taskbar theme). Events are raised on that thread.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const string WindowClass = "DazeMediaOsd.Tray";
    private const uint IconId = 1;
    private const uint CallbackMessage = PInvoke.WM_APP + 1;
    private const uint ExitCommand = 1;
    private const uint SettingsCommand = 2;
    private const uint FirstDeviceCommand = 100; // + index into the menu's device list
    private const uint NinKeySelect = PInvoke.NIN_SELECT | 0x1; // NINF_KEY; not in the Win32 metadata

    private readonly IAudioService? _audio;
    private readonly string _tooltip;
    private readonly string _darkTaskbarIcon;
    private readonly string _lightTaskbarIcon;
    private readonly Thread _thread;
    private readonly WNDPROC _wndProc; // the window class holds only a native pointer: keep the delegate alive
    private uint _taskbarCreated;
    private HWND _window;
    private HICON _icon;

    /// <param name="darkTaskbarIcon">.ico drawn for a dark taskbar (light glyph).</param>
    /// <param name="lightTaskbarIcon">.ico drawn for a light taskbar (dark glyph).</param>
    /// <param name="audio">Lists devices for the Output/Input submenus and switches to the one picked; none without it.</param>
    public TrayIcon(string tooltip, string darkTaskbarIcon, string lightTaskbarIcon, IAudioService? audio = null)
    {
        _audio = audio;
        _tooltip = tooltip;
        _darkTaskbarIcon = darkTaskbarIcon;
        _lightTaskbarIcon = lightTaskbarIcon;
        _wndProc = WndProc;
        using var ready = new ManualResetEventSlim();
        Exception? failure = null;
        _thread = new Thread(() =>
        {
            try
            {
                CreateWindow();
                if (!Add())
                    throw new InvalidOperationException("Shell_NotifyIcon(NIM_ADD) failed.");
            }
            catch (InvalidOperationException e)
            {
                failure = e;
                // No message loop will run to handle Dispose's WM_CLOSE: clean up on this thread.
                if (_window != HWND.Null)
                {
                    _ = PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_DELETE, Data(0)); // no-op if NIM_ADD failed
                    PInvoke.DestroyWindow(_window);
                    _window = HWND.Null;
                }
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
            Name = "Tray",
        };
        _thread.Start();
        ready.Wait();
        if (failure is not null)
        {
            Dispose(); // window already gone; frees the icon handle
            throw failure;
        }
    }

    /// <summary>Exit chosen in the menu. Raised on the tray thread.</summary>
    public event EventHandler? ExitRequested;

    /// <summary>Settings chosen in the menu, or the icon clicked / selected with the keyboard. Raised on the tray thread.</summary>
    public event EventHandler? SettingsRequested;

    private unsafe void CreateWindow()
    {
        var instance = (HINSTANCE)(nint)PInvoke.GetModuleHandle((PCWSTR)null).Value;
        _taskbarCreated = PInvoke.RegisterWindowMessage("TaskbarCreated");
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
            // Never shown; top-level only so that broadcasts reach it.
            _window = PInvoke.CreateWindowEx(WINDOW_EX_STYLE.WS_EX_TOOLWINDOW, className, null, 0, 0, 0, 0, 0,
                HWND.Null, HMENU.Null, instance, null);
        }
        if (_window == HWND.Null)
            throw new InvalidOperationException("CreateWindowEx failed.");
    }

    private static void RunMessageLoop()
    {
        while (PInvoke.GetMessage(out var message, HWND.Null, 0, 0) > 0)
            PInvoke.DispatchMessage(message);
    }

    private bool Add()
    {
        UpdateIconHandle();
        var data = Data(NOTIFY_ICON_DATA_FLAGS.NIF_MESSAGE | NOTIFY_ICON_DATA_FLAGS.NIF_ICON
            | NOTIFY_ICON_DATA_FLAGS.NIF_TIP | NOTIFY_ICON_DATA_FLAGS.NIF_SHOWTIP);
        data.uCallbackMessage = CallbackMessage;
        data.szTip = _tooltip;
        if (!PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_ADD, in data))
            return false;
        // Version 4: WM_CONTEXTMENU for right-click and Shift+F10, anchor in wParam.
        data.Anonymous.uVersion = PInvoke.NOTIFYICON_VERSION_4;
        return PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_SETVERSION, in data);
    }

    private NOTIFYICONDATAW Data(NOTIFY_ICON_DATA_FLAGS flags) => new()
    {
        cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
        hWnd = _window,
        uID = IconId,
        uFlags = flags,
        hIcon = _icon,
    };

    /// <summary>Loads the icon for the current taskbar theme at the small-icon size for the system DPI.</summary>
    private unsafe void UpdateIconHandle()
    {
        var size = PInvoke.GetSystemMetricsForDpi(SYSTEM_METRICS_INDEX.SM_CXSMICON, PInvoke.GetDpiForSystem());
        var path = TaskbarIsLight() ? _lightTaskbarIcon : _darkTaskbarIcon;
        HICON icon;
        fixed (char* file = path)
            icon = (HICON)(nint)PInvoke.LoadImage(HINSTANCE.Null, file, GDI_IMAGE_TYPE.IMAGE_ICON, size, size,
                IMAGE_FLAGS.LR_LOADFROMFILE).Value;
        if (icon == HICON.Null)
        {
            // Shell_NotifyIcon still adds an empty slot, which keeps the menu reachable.
            Log.Info($"tray icon {path} failed to load");
            return;
        }
        if (_icon != HICON.Null)
            PInvoke.DestroyIcon(_icon);
        _icon = icon;
    }

    private static bool TaskbarIsLight() =>
        Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
            "SystemUsesLightTheme", 0) is int light && light != 0;

    private LRESULT WndProc(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam)
    {
        switch (message)
        {
            case CallbackMessage:
                // Version 4: LOWORD(lParam) is the event, wParam the anchor in screen coordinates.
                var notification = (uint)(lParam.Value & 0xFFFF);
                if (notification == PInvoke.WM_CONTEXTMENU)
                    ShowMenu((short)(wParam.Value & 0xFFFF), (short)((wParam.Value >> 16) & 0xFFFF));
                else if (notification is PInvoke.NIN_SELECT or NinKeySelect)
                    Raise(SettingsRequested);
                return (LRESULT)0;
            case PInvoke.WM_SETTINGCHANGE when ImmersiveColorSetChanged(lParam):
                UpdateIconHandle();
                _ = PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_MODIFY, Data(NOTIFY_ICON_DATA_FLAGS.NIF_ICON));
                return (LRESULT)0;
            case PInvoke.WM_CLOSE:
                _ = PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_DELETE, Data(0));
                PInvoke.DestroyWindow(hwnd);
                return (LRESULT)0;
            case PInvoke.WM_DESTROY:
                PInvoke.PostQuitMessage(0);
                return (LRESULT)0;
        }
        if (message == _taskbarCreated && _taskbarCreated != 0)
        {
            // Explorer restarted: the old icon is gone.
            Log.Info($"taskbar re-created; tray icon re-added: {Add()}");
            return (LRESULT)0;
        }
        return PInvoke.DefWindowProc(hwnd, message, wParam, lParam);
    }

    private static bool ImmersiveColorSetChanged(LPARAM lParam) =>
        lParam.Value != 0 && Marshal.PtrToStringUni(lParam.Value) == "ImmersiveColorSet";

    private unsafe void ShowMenu(int x, int y)
    {
        // Win32 menus are light-only unless uxtheme is told otherwise; follow the taskbar's theme.
        UndocumentedMethods.SetPreferredAppMode(TaskbarIsLight()
            ? UndocumentedMethods.PreferredAppMode.ForceLight
            : UndocumentedMethods.PreferredAppMode.ForceDark);
        UndocumentedMethods.FlushMenuThemes();

        var menu = PInvoke.CreatePopupMenu();
        var devices = new List<(AudioEndpoint Endpoint, EndpointFlow Flow)>();
        try
        {
            if (_audio is not null)
            {
                AppendDevices(menu, "Output", EndpointFlow.Output, devices);
                AppendDevices(menu, "Input", EndpointFlow.Input, devices);
                PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_SEPARATOR, 0, (PCWSTR)null);
            }
            fixed (char* settings = "Settings")
                PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_STRING, SettingsCommand, settings);
            PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_SEPARATOR, 0, (PCWSTR)null);
            fixed (char* exit = "Exit")
                PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_STRING, ExitCommand, exit);
            // Bold = what a left-click does, as in other tray menus.
            PInvoke.SetMenuDefaultItem(menu, SettingsCommand, 0);
            // Required for the menu to close when the user clicks elsewhere (KB135788).
            PInvoke.SetForegroundWindow(_window);
            var command = PInvoke.TrackPopupMenuEx(menu,
                (uint)(TRACK_POPUP_MENU_FLAGS.TPM_RETURNCMD | TRACK_POPUP_MENU_FLAGS.TPM_NONOTIFY
                    | TRACK_POPUP_MENU_FLAGS.TPM_RIGHTBUTTON | TRACK_POPUP_MENU_FLAGS.TPM_BOTTOMALIGN),
                x, y, _window, null);
            PInvoke.PostMessage(_window, PInvoke.WM_NULL, default, default);
            if (command.Value == ExitCommand)
                Raise(ExitRequested);
            else if (command.Value == SettingsCommand)
                Raise(SettingsRequested);
            else if (command.Value >= FirstDeviceCommand && command.Value - FirstDeviceCommand < devices.Count)
            {
                var (endpoint, flow) = devices[(int)(command.Value - FirstDeviceCommand)];
                Log.Info($"tray: default {flow} -> \"{endpoint.Name}\"");
                _audio!.SetDefault(endpoint.Id, flow);
            }
        }
        finally
        {
            PInvoke.DestroyMenu(menu); // destroys the submenus too
        }
    }

    /// <summary>A submenu of the flow's switchable devices, the default checked; picking the default does nothing.</summary>
    private unsafe void AppendDevices(HMENU menu, string title, EndpointFlow flow, List<(AudioEndpoint, EndpointFlow)> devices)
    {
        var currentId = _audio!.Current.Get(flow)?.Id;
        var choices = DeviceChoices.For(_audio.Endpoints(flow), currentId);
        var submenu = PInvoke.CreatePopupMenu();
        if (choices.Count == 0)
        {
            fixed (char* none = "No devices")
                PInvoke.AppendMenu(submenu, MENU_ITEM_FLAGS.MF_STRING | MENU_ITEM_FLAGS.MF_GRAYED, 0, none);
        }
        foreach (var endpoint in choices)
        {
            var current = endpoint.Id == currentId;
            var flags = MENU_ITEM_FLAGS.MF_STRING | (current ? MENU_ITEM_FLAGS.MF_CHECKED : 0);
            // Current default: command 0 is what TrackPopupMenuEx returns for "nothing picked" anyway.
            var command = current ? 0 : FirstDeviceCommand + (uint)devices.Count;
            if (!current)
                devices.Add((endpoint, flow));
            fixed (char* name = endpoint.Name.Replace("&", "&&", StringComparison.Ordinal)) // "&" marks an access key
                PInvoke.AppendMenu(submenu, flags, command, name);
        }
        fixed (char* text = title)
            PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_POPUP, (nuint)submenu.Value, text);
    }

    private void Raise(EventHandler? handler)
    {
        try
        {
            handler?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Log.Error("tray menu handler", e);
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
        if (_icon != HICON.Null)
        {
            PInvoke.DestroyIcon(_icon);
            _icon = HICON.Null;
        }
    }
}
