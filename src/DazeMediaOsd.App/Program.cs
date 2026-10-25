using DazeMediaOsd.Core.Diagnostics;
using DazeMediaOsd.Core.Lifecycle;
using DazeMediaOsd.Core.NativeFlyout;
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
        RunInstallerHooks();
        var launch = LaunchArgs.Parse(args);
        if (launch.Mode == LaunchMode.Launcher)
            return Launcher.Run();
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
    /// process (D-029). Before each of these Velopack kills launcher and app together, which leaves the
    /// native flyout minimized (D-006), so every hook restores it. Outside an install it returns at once.
    /// </summary>
    private static void RunInstallerHooks() =>
        VelopackApp.Build()
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
