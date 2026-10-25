using DazeMediaOsd.Core.Diagnostics;
using DazeMediaOsd.Core.Lifecycle;
using DazeMediaOsd.Core.NativeFlyout;
using DazeMediaOsd.Core.Updates;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Velopack;

namespace DazeMediaOsd.App;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        Log.Initialize(Log.DefaultDirectory);
        // Read before RunInstallerHooks: VelopackApp.Run() clears it.
        var restartedByUpdater = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("VELOPACK_RESTART"));
        RunInstallerHooks();
        var launch = LaunchArgs.Parse(args);
        if (launch.Mode == LaunchMode.Launcher)
        {
            // A downloaded update applies here, before the app starts (D-030); exits if it does.
            VelopackUpdateClient.ApplyPendingAtStart(restartedByUpdater);
            return Launcher.Run();
        }
        if (launch.Mode == LaunchMode.Restore)
        {
            FlyoutHost.RestoreAll();
            return 0;
        }

        using var instance = Launcher.ClaimAppInstance();
        if (instance is null)
            return 0; // an app is already running (possibly orphaned by a killed launcher)

        App? app = null;
        try
        {
            WinRT.ComWrappersSupport.InitializeComWrappers();
            Application.Start(_ =>
            {
                SynchronizationContext.SetSynchronizationContext(
                    new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
                app = new App(launch);
            });
            return 0;
        }
        finally
        {
            // Clean exit or managed exception; a hard kill skips this and the launcher restores instead.
            app?.Dispose();
            FlyoutHost.RestoreAll();
        }
    }

    /// <summary>
    /// Velopack setup/update/uninstall call the exe with a hook argument; Run() handles it and exits the
    /// process (D-029). Velopack kills launcher and app together before install and uninstall, which
    /// leaves the native flyout minimized (D-006), so every hook restores it. An update first runs the old
    /// version's before-update hook, which asks the app to exit cleanly so nothing is killed. Outside an
    /// install it returns at once.
    /// </summary>
    private static void RunInstallerHooks() =>
        VelopackApp.Build()
            // Its own apply-on-start would also fire in `--app` / `--restore` and restart with those args;
            // the launcher applies instead (D-030).
            .SetAutoApplyOnStartup(false)
            .OnBeforeUpdateFastCallback(v =>
            {
                // 15 s hook budget: 10 s for a clean exit, then restore anyway.
                if (ExitSignal.Request() && !Launcher.WaitUntilStopped(TimeSpan.FromSeconds(10)))
                    Log.Info("installer: app didn't exit in 10 s, the updater will kill it");
                OnHook($"replacing {v} with an update"); // v is the version being replaced
            })
            .OnAfterInstallFastCallback(v => OnHook($"installed {v}"))
            .OnAfterUpdateFastCallback(v => OnHook($"updated to {v}"))
            .OnBeforeUninstallFastCallback(v =>
            {
                OnHook($"uninstalling {v}");
                if (new Autostart().DisableIfUnder(AppContext.BaseDirectory))
                    Log.Info("installer: removed Start with Windows entry");
            })
            .Run();

    private static void OnHook(string what)
    {
        Log.Info($"installer: {what}, restoring native flyout");
        FlyoutHost.RestoreAll();
    }
}
