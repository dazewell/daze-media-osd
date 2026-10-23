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
    private SettingsStore? _settings;
    private SettingsWindow? _settingsWindow;

    public App(LaunchArgs args)
    {
        _args = args;
        InitializeComponent();
        // Tray-resident: no window has to be open for the app to keep running.
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
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
        // Raised on the UI thread (the settings window edits it). Show the result right away.
        _settings.Changed += (_, settings) =>
        {
            Log.Info($"settings changed: {settings}");
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

        // Without a tray icon the OSD still works; only exiting needs scripts/stop.ps1.
        try
        {
            var assets = Path.Combine(AppContext.BaseDirectory, "Assets");
            _tray = new TrayIcon("Daze Media OSD", Path.Combine(assets, "tray-dark.ico"), Path.Combine(assets, "tray-light.ico"));
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

    private void OpenSettings()
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(new SettingsViewModel(_settings!));
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
        if (state.Reason is OsdReason.DeviceConnected or OsdReason.DeviceDisconnected
            && _settings?.Current.ShowOnDeviceConnect == false)
            return;
        Log.Info($"show {state.Describe()}");
        _window?.Show(state);
    }

    /// <summary>Unhooks and restores the native flyout. Called by Program after the dispatcher stops.</summary>
    public void Dispose()
    {
        _settingsWindow?.Close();
        _tray?.Dispose();
        _osd?.Dispose();
        _keys?.Dispose();
        _media?.Dispose();
        _audio?.Dispose();
        _suppressor?.Dispose();
        _suppressor = null;
        _launcher?.Dispose();
    }
}
