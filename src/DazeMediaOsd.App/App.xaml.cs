using System.Diagnostics;
using DazeMediaOsd.Core.Audio;
using DazeMediaOsd.Core.Diagnostics;
using DazeMediaOsd.Core.Input;
using DazeMediaOsd.Core.Lifecycle;
using DazeMediaOsd.Core.NativeFlyout;
using DazeMediaOsd.Core.Osd;
using DazeMediaOsd.Core.Tray;
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
    private OsdController? _osd;
    private OsdWindow? _window;
    private TrayIcon? _tray;

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
            _window = new OsdWindow();
        }
        catch (InvalidOperationException e)
        {
            Log.Error("OSD window failed to start; exiting without suppressing the native flyout", e);
            Exit();
            return;
        }
        _osd = new OsdController(_audio, _keys);
        _osd.ShowRequested += (_, state) => ui.TryEnqueue(() => Show(state));

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

    private void Show(OsdState state)
    {
        Log.Info($"show {state.Describe()}");
        _window?.Show(state);
    }

    /// <summary>Unhooks and restores the native flyout. Called by Program after the dispatcher stops.</summary>
    public void Dispose()
    {
        _tray?.Dispose();
        _osd?.Dispose();
        _keys?.Dispose();
        _audio?.Dispose();
        _suppressor?.Dispose();
        _suppressor = null;
        _launcher?.Dispose();
    }
}
