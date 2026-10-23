using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;

namespace DazeMediaOsd.Core.NativeFlyout;

public enum SuppressionMode
{
    /// <summary>Host kept minimized for the app's lifetime: the native flyout never draws (D-006).</summary>
    Minimize,

    /// <summary>Host hidden on each show; may flash. Used when no launcher guards the restore (D-005).</summary>
    HidePerShow,
}

/// <summary>
/// Keeps the native volume flyout suppressed while the app runs, including a host that explorer
/// re-creates after a restart. Create, use and dispose on the UI thread: the WinEvent hook is
/// delivered through that thread's message loop.
/// </summary>
public sealed class FlyoutSuppressor : IDisposable
{
    private readonly WINEVENTPROC _callback; // the hook holds only a native pointer: keep the delegate alive
    private HWINEVENTHOOK _hook;

    public FlyoutSuppressor(SuppressionMode mode)
    {
        Mode = mode;
        _callback = OnWinEvent;
        // Process 0 = all processes, so an explorer restart (new pid) needs no re-hook.
        _hook = PInvoke.SetWinEventHook(PInvoke.EVENT_OBJECT_CREATE, PInvoke.EVENT_OBJECT_SHOW, HMODULE.Null,
            _callback, 0, 0, PInvoke.WINEVENT_OUTOFCONTEXT | PInvoke.WINEVENT_SKIPOWNPROCESS);
        if (_hook == HWINEVENTHOOK.Null)
            throw new InvalidOperationException("SetWinEventHook failed.");
        if (mode == SuppressionMode.Minimize)
        {
            foreach (var host in FlyoutHost.FindAll())
                FlyoutHost.Minimize((HWND)host);
        }
        else
        {
            // A host left minimized by an earlier double kill raises SHOW only sometimes: clear it first.
            FlyoutHost.RestoreAll();
        }
    }

    public SuppressionMode Mode { get; private set; }

    /// <summary>The launcher is gone, so nothing would restore a minimized host if we crash now.</summary>
    public void FallBackToHidePerShow()
    {
        Mode = SuppressionMode.HidePerShow;
        FlyoutHost.RestoreAll();
    }

    private void OnWinEvent(HWINEVENTHOOK hook, uint @event, HWND hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != 0 || idChild != 0 || hwnd == HWND.Null) // OBJID_WINDOW, CHILDID_SELF
            return;
        if (@event == PInvoke.EVENT_OBJECT_DESTROY) // inside the hooked range, never interesting
            return;
        if (FlyoutHost.ClassName(hwnd) != FlyoutHostMatcher.HostClass || !FlyoutHost.IsFlyoutHost(hwnd))
            return;

        if (Mode == SuppressionMode.Minimize)
        {
            // A re-created host may not have its band at CREATE yet; then its first SHOW catches it.
            if (!FlyoutHost.IsMinimized(hwnd))
                FlyoutHost.Minimize(hwnd);
        }
        else if (@event == PInvoke.EVENT_OBJECT_SHOW)
        {
            FlyoutHost.Hide(hwnd);
        }
    }

    public void Dispose()
    {
        if (_hook != HWINEVENTHOOK.Null)
        {
            PInvoke.UnhookWinEvent(_hook);
            _hook = HWINEVENTHOOK.Null;
        }
        FlyoutHost.RestoreAll();
    }
}
