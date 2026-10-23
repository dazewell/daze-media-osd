using DazeMediaOsd.Core.Audio;
using DazeMediaOsd.Core.Settings;

namespace DazeMediaOsd.Core.Input;

/// <summary>
/// One registered chord and what it does: <paramref name="Device"/> null cycles the output (D-020), else it
/// makes that device the default (D-022). <paramref name="Id"/> is its <see cref="GlobalHotkey"/> id.
/// </summary>
public sealed record Shortcut(int Id, Hotkey Hotkey, DeviceHotkey? Device);

/// <summary>What a device shortcut press does, given the devices active right now.</summary>
public enum DevicePress
{
    Switch,
    /// <summary>Already the default: show the OSD as the answer.</summary>
    AlreadyDefault,
    /// <summary>Unplugged, off or disabled: nothing to switch to; the OSD says so (D-022).</summary>
    NotActive,
}

/// <summary>Settings → the chords to register, and a press → what to do.</summary>
public static class Shortcuts
{
    public const int CycleOutputId = 1;

    /// <summary>Row <paramref name="index"/> of <see cref="AppSettings.DeviceHotkeys"/>; Settings looks errors up by it.</summary>
    public static int DeviceId(int index) => CycleOutputId + 1 + index;

    /// <summary>Every chord to register: the cycle shortcut, then each row with a device and a chord.</summary>
    public static IReadOnlyList<Shortcut> From(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var shortcuts = new List<Shortcut>();
        if (Hotkey.TryParse(settings.CycleOutputHotkey, out var cycle))
            shortcuts.Add(new Shortcut(CycleOutputId, cycle, null));
        for (var i = 0; i < settings.DeviceHotkeys.Count; i++)
        {
            var row = settings.DeviceHotkeys[i];
            if (row.Id.Length > 0 && Hotkey.TryParse(row.Hotkey, out var hotkey))
                shortcuts.Add(new Shortcut(DeviceId(i), hotkey, row));
        }
        return shortcuts;
    }

    /// <param name="active">The active endpoints of the device's flow.</param>
    /// <param name="defaultId">The current default endpoint of that flow.</param>
    public static DevicePress Press(DeviceHotkey device, IReadOnlyList<AudioEndpoint> active, string? defaultId)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(active);
        return device.Id == defaultId ? DevicePress.AlreadyDefault
            : active.Any(e => e.Id == device.Id) ? DevicePress.Switch
            : DevicePress.NotActive;
    }
}
