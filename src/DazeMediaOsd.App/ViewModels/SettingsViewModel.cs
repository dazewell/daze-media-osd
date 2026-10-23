using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DazeMediaOsd.Core.Audio;
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
    private readonly IAudioService _audio;
    private IReadOnlyList<AudioEndpoint> _outputs = [];
    private IReadOnlyList<AudioEndpoint> _inputs = [];
    private int _recording; // recorder boxes with focus; normally 0 or 1, briefly 2 while focus moves
    private bool _readingAutostart;

    /// <param name="hotkey">Reports whether each shortcut registered; null when the app runs without it.</param>
    /// <param name="audio">Lists the devices a device shortcut can pick.</param>
    public SettingsViewModel(SettingsStore store, Autostart autostart, string exePath, GlobalHotkey? hotkey, IAudioService audio)
    {
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(autostart);
        _store = store;
        _autostart = autostart;
        _exePath = exePath;
        _hotkey = hotkey;
        _audio = audio;
        ReadAutostart();
        var current = store.Current;
        // Each setter saves back the value it just read: Update sees no change and does nothing.
        PositionIndex = Array.IndexOf(PositionOrder, current.Position);
        HideAfterSeconds = current.HideAfterSeconds;
        ShowInput = current.ShowInput;
        ShowMedia = current.ShowMedia;
        ShowOnDeviceConnect = current.ShowOnDeviceConnect;
        CycleOutputHotkey = current.CycleOutputHotkey;
        HotkeyNote = RegistrationNote(Shortcuts.CycleOutputId);
        ReadEndpoints();
        foreach (var row in current.DeviceHotkeys)
            DeviceHotkeys.Add(new DeviceHotkeyRowViewModel(this, row));
        // Rows of connected devices take their current name (renamed in Sound settings): the last-known one.
        SaveDeviceHotkeys();
        // A failure from an earlier, closed window still applies until a save succeeds.
        SaveError = store.SaveError;
    }

    /// <summary>The position ComboBox's items, in screen order (not <see cref="OsdPosition"/>'s, which is stored).</summary>
    private static readonly OsdPosition[] PositionOrder =
    [
        OsdPosition.TopLeft, OsdPosition.TopCenter, OsdPosition.TopRight,
        OsdPosition.BottomLeft, OsdPosition.BottomCenter, OsdPosition.BottomRight,
    ];

    /// <summary>Index into the position ComboBox, see <see cref="PositionOrder"/>.</summary>
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
            Update(s => s with { Position = PositionOrder[value] });
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
    public partial string CycleOutputHotkey { get; set; }

    public static string DefaultCycleOutputHotkey => AppSettings.DefaultCycleOutputHotkey;

    /// <summary>The cycle box recorded <paramref name="chord"/> ("" = off); refused when a device shortcut has it (D-022).</summary>
    public void RecordCycleOutput(object sender, string chord)
    {
        if (UsedBy(chord, null) is { } note)
        {
            HotkeyNote = note;
            return;
        }
        CycleOutputHotkey = chord;
        // The same chord again doesn't run the change handler: clear an earlier refusal here.
        HotkeyNote = RegistrationNote(Shortcuts.CycleOutputId);
    }

    /// <summary>Keys were pressed that make no shortcut: say what would.</summary>
    public void RejectHotkey() => HotkeyNote = InvalidHotkeyNote;

    /// <summary>
    /// While any recorder box has focus, no shortcut may fire (D-021, D-022): pressing one there must reach
    /// the box.
    /// </summary>
    public void Recording(object sender, bool recording)
    {
        _recording = recording ? _recording + 1 : Math.Max(0, _recording - 1);
        _hotkey?.Pause(_recording > 0);
        // A refusal note ("already switches to …") is for the box that just recorded; once focus leaves, back
        // to what still applies.
        if (!recording)
            RefreshNotes();
    }

    /// <summary>The window closed: resume the shortcuts even if a box never saw its focus leave.</summary>
    public void EndRecording()
    {
        _recording = 0;
        _hotkey?.Pause(false);
    }

    // Without the hotkey window no edit can take effect: keep saying so instead of looking healthy.
    internal string? RegistrationNote(int id) =>
        _hotkey is null ? "Shortcuts couldn't start. Restart the app, or see the log."
        : _hotkey.Errors.TryGetValue(id, out var error) ? error
        : null;

    internal const string InvalidHotkeyNote =
        "Use Win, Ctrl or Alt with a letter, number or function key, like Win+Alt+A.";

    /// <summary>One row per device shortcut, in settings order: row i registers as <see cref="Shortcuts.DeviceId"/>(i).</summary>
    public ObservableCollection<DeviceHotkeyRowViewModel> DeviceHotkeys { get; } = [];

    public void AddDeviceHotkey()
    {
        ReadEndpoints(); // a device connected since the window opened
        foreach (var row in DeviceHotkeys)
            row.RefreshOptions();
        DeviceHotkeys.Add(new DeviceHotkeyRowViewModel(this, new DeviceHotkey()));
        SaveDeviceHotkeys();
    }

    internal void Remove(DeviceHotkeyRowViewModel row)
    {
        DeviceHotkeys.Remove(row);
        SaveDeviceHotkeys();
    }

    /// <summary>Why <paramref name="chord"/> can't go to <paramref name="row"/> (null: the cycle box): one chord does one thing.</summary>
    internal string? UsedBy(string chord, DeviceHotkeyRowViewModel? row)
    {
        if (chord.Length == 0)
            return null;
        if (row is not null && chord == CycleOutputHotkey)
            return $"{chord} already switches to the next output. Try another one.";
        return DeviceHotkeys.FirstOrDefault(r => r != row && r.Hotkey == chord) is { } other
            ? $"{chord} already switches to {(other.Selected is { } device ? device.Name : "another device")}. Try another one."
            : null;
    }

    /// <summary>The devices a row can pick: active ones as the tray offers them (<see cref="DeviceChoices"/>), outputs first.</summary>
    internal List<DeviceOption> Choices(string? rowId) =>
        DeviceChoices.For(_outputs, rowId).Select(e => new DeviceOption(e.Id, EndpointFlow.Output, e.Name, Active: true))
            .Concat(DeviceChoices.For(_inputs, rowId).Select(e => new DeviceOption(e.Id, EndpointFlow.Input, e.Name, Active: true)))
            .ToList();

    // Endpoints waits (briefly) on the audio worker: only on open and on Add.
    private void ReadEndpoints()
    {
        _outputs = _audio.Endpoints(EndpointFlow.Output);
        _inputs = _audio.Endpoints(EndpointFlow.Input);
    }

    internal void SaveDeviceHotkeys()
    {
        Update(s => s with { DeviceHotkeys = DeviceHotkeys.Select(r => r.ToModel()).ToList() });
        RefreshNotes();
    }

    // Ids follow row positions, so every row's registration result may have moved.
    private void RefreshNotes()
    {
        HotkeyNote = RegistrationNote(Shortcuts.CycleOutputId);
        for (var i = 0; i < DeviceHotkeys.Count; i++)
            DeviceHotkeys[i].Note = DeviceHotkeys[i].StatusNote(RegistrationNote(Shortcuts.DeviceId(i)));
    }

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
        HotkeyNote = RegistrationNote(Shortcuts.CycleOutputId);
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
            error = "Couldn't change this setting. Try again, or see the log for details.";
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
            readError = "Couldn't check if this is on. See the log for details.";
        }
        _readingAutostart = true;
        StartWithWindows = status.State == AutostartState.On;
        _readingAutostart = false;
        AutostartNote = readError ?? status switch
        {
            { State: AutostartState.DisabledByWindows } => "Turned off in Task Manager. Turn it on here to start with Windows again.",
            { State: AutostartState.On, Path: var path } when !string.Equals(path, _exePath, StringComparison.OrdinalIgnoreCase)
                => $"Windows starts a copy in another folder: {path}. Turn this off and on to use this one.",
            _ => null,
        };
    }

    private void Update(Func<AppSettings, AppSettings> change)
    {
        _store.Update(change);
        SaveError = _store.SaveError;
    }
}
