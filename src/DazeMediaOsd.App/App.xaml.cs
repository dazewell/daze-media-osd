using System.Diagnostics;
using DazeMediaOsd.App.ViewModels;
using DazeMediaOsd.Core.Audio;
using DazeMediaOsd.Core.Diagnostics;
using DazeMediaOsd.Core.Input;
using DazeMediaOsd.Core.Lifecycle;
using DazeMediaOsd.Core.Media;
using DazeMediaOsd.Core.NativeFlyout;
using DazeMediaOsd.Core.Osd;
using DazeMediaOsd.Core.Settings;
using DazeMediaOsd.Core.Tray;
using DazeMediaOsd.Core.Windowing;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace DazeMediaOsd.App;

public sealed partial class App : Application, IDisposable
{
    private readonly LaunchArgs _args;
    private FlyoutSuppressor? _suppressor;
    private Process? _launcher;
    private CoreAudioService? _audio;
    private RawInputKeySource? _keys;
    private GsmtcMediaService? _media;
    private OsdController? _osd;
    private OsdWindow? _window;
    private TrayIcon? _tray;
    private ExitSignal? _exitSignal;
    private SettingsStore? _settings;
    private SettingsWindow? _settingsWindow;
    private GlobalHotkey? _hotkey;
    private DeviceConnector? _connector;
    private volatile IReadOnlyList<Shortcut> _shortcuts = []; // read on the hotkey thread

    public App(LaunchArgs args)
    {
        _args = args;
        InitializeComponent();
        // Tray-resident: no window has to be open for the app to keep running.
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
        // Logged, not handled: XAML then fail-fasts and the launcher restores the native flyout (D-006).
        UnhandledException += (_, e) => Log.Error($"unhandled exception: {e.Message}", e.Exception);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var ui = DispatcherQueue.GetForCurrentThread();
        _settings = new SettingsStore(SettingsStore.DefaultDirectory);
        // Services first: suppressing the native flyout is only right once ours can take over. If they
        // can't start, exit with Windows untouched (fail-open); the launcher then exits too.
        try
        {
            _audio = new CoreAudioService();
            _keys = new RawInputKeySource();
        }
        catch (InvalidOperationException e)
        {
            Log.Error("audio or key source failed to start; exiting without suppressing the native flyout", e);
            Exit();
            return;
        }
        try
        {
            _window = new OsdWindow(_settings.Current);
        }
        catch (InvalidOperationException e)
        {
            Log.Error("OSD window failed to start; exiting without suppressing the native flyout", e);
            Exit();
            return;
        }
        // Media info is optional: the service starts in the background and only logs if GSMTC fails.
        _media = new GsmtcMediaService();
        _osd = new OsdController(_audio, _keys, _media);
        _osd.ShowRequested += (_, state) => ui.TryEnqueue(() => Show(state));
        _osd.RefreshRequested += (_, state) => ui.TryEnqueue(() => _window.Refresh(state));
        _connector = new DeviceConnector(_audio);
        _connector.TimedOut += (_, device) => ShowAnswer(device, PressAnswer.ConnectFailed, ui);
        // Optional like the tray: without it only the hotkey is missing.
        try
        {
            _hotkey = new GlobalHotkey();
            _hotkey.Pressed += (_, id) => OnShortcut(id, ui);
            ApplyHotkey(_settings.Current);
        }
        catch (InvalidOperationException e)
        {
            Log.Error("hotkey window failed to start; running without the hotkey", e);
        }
        // Raised on the UI thread (the settings window edits it). Show the result right away.
        _settings.Changed += (_, settings) =>
        {
            Log.Info($"settings changed: {settings}");
            ApplyHotkey(settings);
            _window.Apply(settings);
            Show(_osd.Snapshot(OsdReason.SettingsPreview));
        };

        // Watch the launcher before choosing a mode: minimizing is only safe while a live launcher will
        // restore the host if we die (D-006). If it dies right after, the queued fallback restores.
        _launcher = Launcher.WatchLauncher(_args.LauncherPid, () => ui.TryEnqueue(() =>
        {
            Log.Info("no launcher: native flyout restored, hiding it per show from now on");
            _suppressor?.FallBackToHidePerShow();
        }));
        _suppressor = new FlyoutSuppressor(_launcher is null ? SuppressionMode.HidePerShow : SuppressionMode.Minimize);
        Log.Info($"app started, suppression={_suppressor.Mode}, {OsdState.Describe(_audio.Current)}");

        // The updater's before-update hook asks us to exit like tray Exit (D-029).
        _exitSignal = ExitSignal.Listen(() => ui.TryEnqueue(() =>
        {
            Log.Info("exit requested by the installer");
            Exit();
        }));

        // Without a tray icon the OSD still works; only exiting needs scripts/stop.ps1.
        try
        {
            var assets = Path.Combine(AppContext.BaseDirectory, "Assets");
            _tray = new TrayIcon("Daze Media OSD", Path.Combine(assets, "tray-dark.ico"), Path.Combine(assets, "tray-light.ico"), _audio);
            _tray.SettingsRequested += (_, _) => ui.TryEnqueue(OpenSettings);
            _tray.ExitRequested += (_, _) => ui.TryEnqueue(() =>
            {
                Log.Info("exit requested from the tray");
                Exit(); // Program's finally disposes us and restores the native flyout (D-006)
            });
        }
        catch (InvalidOperationException e)
        {
            Log.Error("tray icon failed to start; running without it", e);
        }
    }

    // Only on a real change: re-registering the same chords would log for every unrelated setting.
    private void ApplyHotkey(AppSettings settings)
    {
        var shortcuts = Shortcuts.From(settings);
        if (_hotkey is null || shortcuts.SequenceEqual(_shortcuts))
            return;
        _shortcuts = shortcuts;
        _hotkey.Set(shortcuts.ToDictionary(s => s.Id, s => s.Hotkey));
    }

    // Raised on the hotkey thread. Endpoints waits on the audio worker, so stay off the UI thread.
    private void OnShortcut(int id, DispatcherQueue ui)
    {
        // A newer choice wins over a device still connecting (D-023).
        _connector!.Cancel();
        switch (_shortcuts.FirstOrDefault(s => s.Id == id))
        {
            case { Device: null }:
                CycleOutput(ui);
                break;
            case { Device: { } device }:
                SwitchTo(device, ui);
                break;
        }
    }

    private void SwitchTo(DeviceHotkey device, DispatcherQueue ui)
    {
        var press = Shortcuts.Press(device, _audio!.Endpoints(device.Flow), _audio.Current.Get(device.Flow)?.Id);
        Log.Info($"hotkey: default {device.Flow} -> \"{device.Name}\": {press}");
        switch (press)
        {
            case DevicePress.Switch:
                _audio.SetDefault(device.Id, device.Flow); // the OSD shows it as a default-device change
                break;
            case DevicePress.AlreadyDefault:
                ui.TryEnqueue(() => Show(_osd!.Snapshot(OsdReason.Hotkey)));
                break;
            case DevicePress.NotActive:
                // A paired Bluetooth device gets asked to connect; once it does, the connector switches (D-023).
                var result = _connector!.Connect(device);
                Log.Info($"hotkey: connect \"{device.Name}\": {result}");
                if (result != ConnectResult.AlreadyConnected)
                    ShowAnswer(device, result == ConnectResult.Requested ? PressAnswer.Connecting : PressAnswer.NotConnected, ui);
                break;
        }
    }

    private void ShowAnswer(DeviceHotkey device, PressAnswer answer, DispatcherQueue ui)
    {
        var notice = DeviceNotice.ForPress(device.Name, device.Flow, answer);
        ui.TryEnqueue(() =>
        {
            // Checked here, in UI order: the connect show may already have run (or a later press replaced it).
            if (answer == PressAnswer.Connecting && !_connector!.IsWaitingFor(device))
                return;
            Show(_osd!.Snapshot(OsdReason.Hotkey) with { Device = notice });
        });
    }

    private void CycleOutput(DispatcherQueue ui)
    {
        var current = _audio!.Current.Output?.Id;
        if (DeviceCycle.Next(_audio.Endpoints(EndpointFlow.Output), current) is { } next)
        {
            Log.Info($"hotkey: default Output -> \"{next.Name}\"");
            _audio.SetDefault(next.Id, EndpointFlow.Output); // the OSD shows it as OutputDeviceChanged
            return;
        }
        Log.Info("hotkey: no other output to switch to");
        ui.TryEnqueue(() => Show(_osd!.Snapshot(OsdReason.Hotkey)));
    }

    private void OpenSettings()
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(new SettingsViewModel(_settings!, new Autostart(), Environment.ProcessPath!, _hotkey, _audio!));
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Activate();
        }
        WindowActivation.BringToFront(_settingsWindow.Handle);
    }

    private void Show(OsdState state)
    {
        // With the media row off a media key has nothing to show (D-011).
        if (state.Reason == OsdReason.MediaKey && _settings?.Current.ShowMedia == false)
            return;
        if (_settings?.Current.ShowOnDeviceConnect == false)
        {
            if (state.Reason is OsdReason.DeviceConnected or OsdReason.DeviceDisconnected)
                return;
            if (state.Device is { Answer: PressAnswer.None })
                state = state with { Device = null }; // a default switch still shows, without the event line
        }
        Log.Info($"show {state.Describe()}");
        _window?.Show(state);
    }

    /// <summary>Unhooks and restores the native flyout. Called by Program after the dispatcher stops.</summary>
    public void Dispose()
    {
        _settingsWindow?.Close();
        _exitSignal?.Dispose();
        _tray?.Dispose();
        _hotkey?.Dispose();
        _connector?.Dispose();
        _osd?.Dispose();
        _keys?.Dispose();
        _media?.Dispose();
        _audio?.Dispose();
        _suppressor?.Dispose();
        _suppressor = null;
        _launcher?.Dispose();
    }
}
