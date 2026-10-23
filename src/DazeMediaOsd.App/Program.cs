using DazeMediaOsd.Core.Lifecycle;
using DazeMediaOsd.Core.NativeFlyout;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace DazeMediaOsd.App;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
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
}
