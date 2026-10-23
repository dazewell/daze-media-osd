using CommunityToolkit.Mvvm.ComponentModel;
using DazeMediaOsd.Core.Diagnostics;
using DazeMediaOsd.Core.Input;
using DazeMediaOsd.Core.Lifecycle;
using DazeMediaOsd.Core.Settings;

namespace DazeMediaOsd.App.ViewModels;

/// <summary>Two-way view of <see cref="SettingsStore"/>: every edit is saved at once, no Apply button.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsStore _store;
    private readonly Autostart _autostart;
    private readonly string _exePath;
    private readonly GlobalHotkey? _hotkey;
    private bool _readingAutostart;

    /// <param name="hotkey">Reports whether the cycle-output shortcut registered; null when the app runs without it.</param>
    public SettingsViewModel(SettingsStore store, Autostart autostart, string exePath, GlobalHotkey? hotkey)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(autostart);
        _store = store;
        _autostart = autostart;
        _exePath = exePath;
        _hotkey = hotkey;
        ReadAutostart();
        var current = store.Current;
        // Each setter saves back the value it just read: Update sees no change and does nothing.
        PositionIndex = (int)current.Position;
        HideAfterSeconds = current.HideAfterSeconds;
        ShowInput = current.ShowInput;
        ShowMedia = current.ShowMedia;
        ShowOnDeviceConnect = current.ShowOnDeviceConnect;
        CycleOutputHotkey = current.CycleOutputHotkey;
        HotkeyNote = RegistrationNote();
        // A failure from an earlier, closed window still applies until a save succeeds.
        SaveError = store.SaveError;
    }

    /// <summary>Index into the position ComboBox, whose items follow <see cref="OsdPosition"/>'s order.</summary>
    [ObservableProperty]
    public partial int PositionIndex { get; set; }

    [ObservableProperty]
    public partial double HideAfterSeconds { get; set; }

    [ObservableProperty]
    public partial bool ShowInput { get; set; }

    [ObservableProperty]
    public partial bool ShowMedia { get; set; }

    [ObservableProperty]
    public partial bool ShowOnDeviceConnect { get; set; }

    /// <summary>Set while the last edit couldn't be written to disk: it applies only until the app exits.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSaveError))]
    public partial string? SaveError { get; set; }

    public bool HasSaveError => SaveError is not null;

    /// <summary>Mirrors the Run value (D-015); re-read whenever the window opens, never stored.</summary>
    [ObservableProperty]
    public partial bool StartWithWindows { get; set; }

    /// <summary>Why the toggle may not match expectations: off in Task Manager, another copy, or a failed write.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAutostartNote))]
    public partial string? AutostartNote { get; set; }

    public bool HasAutostartNote => AutostartNote is not null;

    partial void OnPositionIndexChanged(int value)
    {
        if (value >= 0)
            Update(s => s with { Position = (OsdPosition)value });
    }

    partial void OnHideAfterSecondsChanged(double value)
    {
        // A cleared NumberBox reports NaN: put the stored value back instead of saving a default.
        if (!double.IsFinite(value))
        {
            HideAfterSeconds = _store.Current.HideAfterSeconds;
            return;
        }
        Update(s => s with { HideAfterSeconds = value });
    }

    partial void OnShowInputChanged(bool value) => Update(s => s with { ShowInput = value });

    partial void OnShowMediaChanged(bool value) => Update(s => s with { ShowMedia = value });

    partial void OnShowOnDeviceConnectChanged(bool value) => Update(s => s with { ShowOnDeviceConnect = value });

    /// <summary>The cycle-output shortcut, recorded from the keys pressed in its box. "" is off.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanResetHotkey), nameof(CanClearHotkey))]
    public partial string CycleOutputHotkey { get; set; }

    public bool CanResetHotkey => CycleOutputHotkey != AppSettings.DefaultCycleOutputHotkey;

    public bool CanClearHotkey => CycleOutputHotkey.Length > 0;

    public static string ResetHotkeyTip => "Reset to " + AppSettings.DefaultCycleOutputHotkey;

    public void ResetHotkey() => CycleOutputHotkey = AppSettings.DefaultCycleOutputHotkey;

    public void ClearHotkey() => CycleOutputHotkey = "";

    /// <summary>While the box records, the current shortcut must not fire (D-021).</summary>
    public void PauseHotkey(bool paused) => _hotkey?.Pause(paused);

    /// <summary>Keys were pressed that make no shortcut: say what would.</summary>
    public void RejectHotkey() => HotkeyNote = InvalidHotkeyNote;

    // Without the hotkey window no edit can take effect: keep saying so instead of looking healthy.
    private string? RegistrationNote() => _hotkey is null ? "The shortcut isn't available: see the log." : _hotkey.Error;

    private const string InvalidHotkeyNote =
        "Not a shortcut. Use Win, Ctrl or Alt with a letter, digit or F1–F24, like Win+Alt+A.";

    /// <summary>Why the shortcut isn't active: not a valid shortcut, or Windows refused it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHotkeyNote))]
    public partial string? HotkeyNote { get; set; }

    public bool HasHotkeyNote => HotkeyNote is not null;

    partial void OnCycleOutputHotkeyChanged(string value)
    {
        var text = value.Trim();
        if (text.Length > 0 && !Hotkey.TryParse(text, out _))
        {
            HotkeyNote = InvalidHotkeyNote;
            return;
        }
        Update(s => s with { CycleOutputHotkey = text });
        // Show what was stored ("alt+win+a" → "Win+Alt+A"); setting it again re-enters with no change.
        CycleOutputHotkey = _store.Current.CycleOutputHotkey;
        HotkeyNote = RegistrationNote();
    }

    partial void OnStartWithWindowsChanged(bool value)
    {
        if (_readingAutostart)
            return;
        string? error = null;
        try
        {
            if (value)
                _autostart.Enable(_exePath);
            else
                _autostart.Disable();
            Log.Info($"autostart: {(value ? "enabled for " + _exePath : "disabled")}");
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            Log.Info($"autostart: change failed: {e.Message}");
            error = "Couldn't change the startup entry: " + e.Message;
        }
        ReadAutostart();
        AutostartNote = error ?? AutostartNote;
    }

    private void ReadAutostart()
    {
        AutostartStatus status;
        string? readError = null;
        try
        {
            status = _autostart.Read();
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            status = new AutostartStatus(AutostartState.Off, null);
            readError = "Couldn't read the startup entry: " + e.Message;
        }
        _readingAutostart = true;
        StartWithWindows = status.State == AutostartState.On;
        _readingAutostart = false;
        AutostartNote = readError ?? status switch
        {
            { State: AutostartState.DisabledByWindows } => "Turned off in Task Manager. Turn it on here to start again.",
            { State: AutostartState.On, Path: var path } when !string.Equals(path, _exePath, StringComparison.OrdinalIgnoreCase)
                => $"Starts another copy: {path}. Turn it off and on to start this one.",
            _ => null,
        };
    }

    private void Update(Func<AppSettings, AppSettings> change)
    {
        _store.Update(change);
        SaveError = _store.SaveError;
    }
}
