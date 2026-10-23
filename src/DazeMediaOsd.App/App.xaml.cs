using System.Diagnostics;
using DazeMediaOsd.Core.Audio;
using DazeMediaOsd.Core.Diagnostics;
using DazeMediaOsd.Core.Input;
using DazeMediaOsd.Core.Lifecycle;
using DazeMediaOsd.Core.NativeFlyout;
using DazeMediaOsd.Core.Osd;
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
        // Minimizing is only safe while a launcher will restore the host if we die (D-006).
        _suppressor = new FlyoutSuppressor(_args.LauncherPid is null ? SuppressionMode.HidePerShow : SuppressionMode.Minimize);
        _launcher = Launcher.WatchLauncher(_args.LauncherPid, () => ui.TryEnqueue(() =>
        {
            Log.Info("launcher gone: native flyout restored, hiding it per show from now on");
            _suppressor?.FallBackToHidePerShow();
        }));

        _audio = new CoreAudioService();
        _keys = new RawInputKeySource();
        _osd = new OsdController(_audio, _keys);
        _osd.ShowRequested += (_, state) => ui.TryEnqueue(() => Show(state));
        Log.Info($"app started, suppression={_suppressor.Mode}, {OsdState.Describe(_audio.Current)}");
    }

    // Placeholder until OsdWindow exists: the log is the only visible output.
    private static void Show(OsdState state) => Log.Info($"show {state.Describe()}");

    /// <summary>Unhooks and restores the native flyout. Called by Program after the dispatcher stops.</summary>
    public void Dispose()
    {
        _osd?.Dispose();
        _keys?.Dispose();
        _audio?.Dispose();
        _suppressor?.Dispose();
        _suppressor = null;
        _launcher?.Dispose();
    }
}
