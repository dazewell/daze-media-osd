using DazeMediaOsd.Core.Settings;

namespace DazeMediaOsd.Core.Input;

/// <summary>Which part of a taskbar the cursor was over when the wheel turned (D-035).</summary>
public enum TaskbarZone
{
    /// <summary>Outside the tray: Start, app buttons, widgets, empty space.</summary>
    Apps,
    /// <summary>Inside the tray (<c>TrayNotifyWnd</c>).</summary>
    Tray,
    /// <summary>The tray's speaker icon: Windows already changes the volume here (Q7), so we stand aside.</summary>
    VolumeIcon,
}

/// <summary>Keys held during a wheel notch. Win is tracked only so a Win+wheel matches nothing.</summary>
[Flags]
public enum HeldModifiers
{
    None = 0,
    Ctrl = 1,
    Shift = 2,
    Alt = 4,
    Win = 8,
}

/// <param name="Delta">Raw wheel delta: ±120 per notch on a classic wheel, smaller steps on high-resolution ones; up is positive.</param>
public sealed class TaskbarWheelEventArgs(TaskbarZone zone, int delta, HeldModifiers held) : EventArgs
{
    public TaskbarZone Zone { get; } = zone;
    public int Delta { get; } = delta;
    public HeldModifiers Held { get; } = held;
}

public interface ITaskbarWheelSource : IDisposable
{
    /// <summary>Raised for wheel turns over a taskbar only, on the source's own thread.</summary>
    event EventHandler<TaskbarWheelEventArgs>? Wheeled;
}

/// <summary>Whether a wheel turn is meant for an action, and how many steps it makes (D-036).</summary>
public static class WheelRules
{
    public const int Notch = 120; // WHEEL_DELTA

    /// <summary>
    /// True when the turn happened in <paramref name="area"/> with exactly <paramref name="modifier"/> held:
    /// Ctrl+wheel must not also fire the plain-wheel action, so another action can own it. Never over the
    /// speaker icon, where Windows already acts.
    /// </summary>
    public static bool Matches(WheelArea area, WheelModifier modifier, TaskbarZone zone, HeldModifiers held) =>
        zone != TaskbarZone.VolumeIcon
        && area switch
        {
            WheelArea.Taskbar => true,
            WheelArea.Apps => zone == TaskbarZone.Apps,
            WheelArea.Tray => zone == TaskbarZone.Tray,
            _ => false,
        }
        && held == modifier switch
        {
            WheelModifier.Ctrl => HeldModifiers.Ctrl,
            WheelModifier.Shift => HeldModifiers.Shift,
            WheelModifier.Alt => HeldModifiers.Alt,
            _ => HeldModifiers.None,
        };

    /// <summary>Whether one wheel turn can be in both areas at once: Taskbar holds every other area, which hold only themselves.</summary>
    public static bool Overlaps(WheelArea a, WheelArea b) =>
        a != WheelArea.Off && b != WheelArea.Off && (a == b || a == WheelArea.Taskbar || b == WheelArea.Taskbar);

    /// <summary>
    /// Whether one wheel turn would run both actions: the same key held and areas that touch. Two actions are
    /// never allowed to share a turn (D-040); an action that is off conflicts with nothing.
    /// </summary>
    public static bool Conflict(WheelArea areaA, WheelModifier modifierA, WheelArea areaB, WheelModifier modifierB) =>
        modifierA == modifierB && Overlaps(areaA, areaB);
}

/// <summary>
/// Adds up wheel deltas into whole notches, so a high-resolution wheel (e.g. 15 per event) steps as evenly as a
/// classic one. A turn the other way drops what was left over. Not thread-safe: one per source thread.
/// </summary>
public sealed class WheelAccumulator
{
    private int _rest;

    /// <summary>Whole notches completed by this delta: positive up, negative down, usually 0 or ±1.</summary>
    public int Add(int delta)
    {
        if (delta == 0)
            return 0;
        if (Math.Sign(delta) != Math.Sign(_rest))
            _rest = 0;
        _rest += delta;
        var notches = _rest / WheelRules.Notch; // truncates toward zero for both signs
        _rest -= notches * WheelRules.Notch;
        return notches;
    }
}
