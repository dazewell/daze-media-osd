using System.Diagnostics;
using DazeMediaOsd.Core.Lifecycle;
using DazeMediaOsd.Core.NativeFlyout;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace DazeMediaOsd.App;

public sealed partial class App : Application, IDisposable
{
    private readonly LaunchArgs _args;
    private FlyoutSuppressor? _suppressor;
    private Process? _launcher;

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
        // Watch first: minimizing is only safe while a live launcher will restore the host if we die
        // (D-006). If it dies right after, the queued fallback runs after this method and restores.
        _launcher = Launcher.WatchLauncher(_args.LauncherPid,
            () => ui.TryEnqueue(() => _suppressor?.FallBackToHidePerShow()));
        _suppressor = new FlyoutSuppressor(_launcher is null ? SuppressionMode.HidePerShow : SuppressionMode.Minimize);
    }

    /// <summary>Unhooks and restores the native flyout. Called by Program after the dispatcher stops.</summary>
    public void Dispose()
    {
        _suppressor?.Dispose();
        _suppressor = null;
        _launcher?.Dispose();
    }
}
