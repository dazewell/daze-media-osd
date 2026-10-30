using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DazeMediaOsd.Core.Audio;
using DazeMediaOsd.Core.Brightness;
using DazeMediaOsd.Core.Diagnostics;
using DazeMediaOsd.Core.Input;
using DazeMediaOsd.Core.Lifecycle;
using DazeMediaOsd.Core.Settings;
using Microsoft.UI.Dispatching;

namespace DazeMediaOsd.App.ViewModels;

/// <summary>Two-way view of <see cref="SettingsStore"/>: every edit is saved at once, no Apply button.</summary>
public sealed partial class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly SettingsStore _store;
    private readonly Autostart _autostart;
    private readonly string _exePath;
    private readonly GlobalHotkey? _hotkey;
    private readonly IAudioService _audio;
    private readonly IBrightnessService? _brightness;
    private readonly DispatcherQueue? _ui;
    private IReadOnlyList<AudioEndpoint> _outputs = [];
    private IReadOnlyList<AudioEndpoint> _inputs = [];
    private int _recording; // recorder boxes with focus; normally 0 or 1, briefly 2 while focus moves
    private bool _readingAutostart;

    /// <param name="hotkey">Reports whether each shortcut registered; null when the app runs without it.</param>
    /// <param name="audio">Lists the devices a device shortcut can pick.</param>
    /// <param name="brightness">Names the screen the brightness wheel works on; null when the app runs without it.</param>
    public SettingsViewModel(SettingsStore store, Autostart autostart, string exePath, GlobalHotkey? hotkey, IAudioService audio,
        IBrightnessService? brightness)
    {
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(autostart);
        _store = store;
        _autostart = autostart;
        _exePath = exePath;
        _hotkey = hotkey;
        _audio = audio;
        _brightness = brightness;
        _ui = DispatcherQueue.GetForCurrentThread();
        ReadAutostart();
        var current = store.Current;
        // Each setter saves back the value it just read: Update sees no change and does nothing.
        PositionIndex = Array.IndexOf(PositionOrder, current.Position);
        HideAfterSeconds = current.HideAfterSeconds;
        ShowInput = current.ShowInput;
        ShowMedia = current.ShowMedia;
        ShowOnDeviceConnect = current.ShowOnDeviceConnect;
        ShowOnMediaChange = current.ShowOnMediaChange;
        CycleOutputHotkey = current.CycleOutputHotkey;
        WheelVolumeAreaIndex = Array.IndexOf(WheelAreaOrder, current.WheelVolumeArea);
        WheelVolumeModifierIndex = (int)current.WheelVolumeModifier;
        WheelVolumeStep = current.WheelVolumeStep;
        WheelBrightnessAreaIndex = Array.IndexOf(WheelAreaOrder, current.WheelBrightnessArea);
        WheelBrightnessModifierIndex = (int)current.WheelBrightnessModifier;
        WheelBrightnessStep = current.WheelBrightnessStep;
        HotkeyNote = RegistrationNote(Shortcuts.CycleOutputId);
        ReadEndpoints();
        foreach (var row in current.DeviceHotkeys)
            DeviceHotkeys.Add(new DeviceHotkeyRowViewModel(this, row));
        // Rows of connected devices take their current name (renamed in Sound settings): the last-known one.
        SaveDeviceHotkeys();
        // A failure from an earlier, closed window still applies until a save succeeds.
        SaveError = store.SaveError;
        (BrightnessScreen, BrightnessScreenNote) = ScreenState();
        if (_brightness is not null)
        {
            _brightness.ScreenChanged += OnBrightnessScreenChanged;
            _brightness.Refresh(); // a lid or a dock may have changed what is there since the last look
        }
    }

    /// <summary>The window closed: stop listening to the brightness service, which outlives it.</summary>
    public void Dispose()
    {
        if (_brightness is not null)
            _brightness.ScreenChanged -= OnBrightnessScreenChanged;
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

    [ObservableProperty]
    public partial bool ShowOnMediaChange { get; set; }

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

    partial void OnShowOnMediaChangeChanged(bool value) => Update(s => s with { ShowOnMediaChange = value });

    /// <summary>The wheel-area ComboBox's items, most to least reach, then off (D-036).</summary>
    private static readonly WheelArea[] WheelAreaOrder = [WheelArea.Taskbar, WheelArea.Apps, WheelArea.Tray, WheelArea.Off];

    /// <summary>Index into the wheel-area ComboBox, see <see cref="WheelAreaOrder"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WheelVolumeOn))]
    public partial int WheelVolumeAreaIndex { get; set; }

    /// <summary>Index into the key ComboBox: <see cref="WheelModifier"/>'s own order.</summary>
    [ObservableProperty]
    public partial int WheelVolumeModifierIndex { get; set; }

    [ObservableProperty]
    public partial double WheelVolumeStep { get; set; }

    /// <summary>The key and step only matter while the wheel does something.</summary>
    public bool WheelVolumeOn => WheelVolumeAreaIndex >= 0 && WheelAreaOrder[WheelVolumeAreaIndex] != WheelArea.Off;

    partial void OnWheelVolumeAreaIndexChanged(int value)
    {
        if (value >= 0)
            ChangeWheel(s => s with { WheelVolumeArea = WheelAreaOrder[value] }, volume: true,
                () => WheelVolumeAreaIndex = Array.IndexOf(WheelAreaOrder, _store.Current.WheelVolumeArea));
    }

    partial void OnWheelVolumeModifierIndexChanged(int value)
    {
        if (value >= 0)
            ChangeWheel(s => s with { WheelVolumeModifier = (WheelModifier)value }, volume: true,
                () => WheelVolumeModifierIndex = (int)_store.Current.WheelVolumeModifier);
    }

    partial void OnWheelVolumeStepChanged(double value)
    {
        // A cleared NumberBox reports NaN: put the stored value back.
        if (!double.IsFinite(value))
        {
            WheelVolumeStep = _store.Current.WheelVolumeStep;
            return;
        }
        Update(s => s with { WheelVolumeStep = (int)Math.Round(value) });
        WheelVolumeStep = _store.Current.WheelVolumeStep; // shows the clamped, whole value
    }

    /// <summary>Index into the wheel-area ComboBox of the brightness card, see <see cref="WheelAreaOrder"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WheelBrightnessOn))]
    public partial int WheelBrightnessAreaIndex { get; set; }

    /// <summary>Index into the key ComboBox: <see cref="WheelModifier"/>'s own order.</summary>
    [ObservableProperty]
    public partial int WheelBrightnessModifierIndex { get; set; }

    [ObservableProperty]
    public partial double WheelBrightnessStep { get; set; }

    /// <summary>The key and step only matter while the wheel does something.</summary>
    public bool WheelBrightnessOn => WheelBrightnessAreaIndex >= 0 && WheelAreaOrder[WheelBrightnessAreaIndex] != WheelArea.Off;

    partial void OnWheelBrightnessAreaIndexChanged(int value)
    {
        if (value >= 0)
            ChangeWheel(s => s with { WheelBrightnessArea = WheelAreaOrder[value] }, volume: false,
                () => WheelBrightnessAreaIndex = Array.IndexOf(WheelAreaOrder, _store.Current.WheelBrightnessArea));
    }

    partial void OnWheelBrightnessModifierIndexChanged(int value)
    {
        if (value >= 0)
            ChangeWheel(s => s with { WheelBrightnessModifier = (WheelModifier)value }, volume: false,
                () => WheelBrightnessModifierIndex = (int)_store.Current.WheelBrightnessModifier);
    }

    partial void OnWheelBrightnessStepChanged(double value)
    {
        // A cleared NumberBox reports NaN: put the stored value back.
        if (!double.IsFinite(value))
        {
            WheelBrightnessStep = _store.Current.WheelBrightnessStep;
            return;
        }
        Update(s => s with { WheelBrightnessStep = (int)Math.Round(value) });
        WheelBrightnessStep = _store.Current.WheelBrightnessStep; // shows the clamped, whole value
    }

    /// <summary>Why the last volume choice was refused: brightness already has that place and key.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWheelVolumeNote))]
    public partial string? WheelVolumeNote { get; set; }

    public bool HasWheelVolumeNote => WheelVolumeNote is not null;

    /// <summary>Why the last brightness choice was refused: volume already has that place and key.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWheelBrightnessNote))]
    public partial string? WheelBrightnessNote { get; set; }

    public bool HasWheelBrightnessNote => WheelBrightnessNote is not null;

    // Two actions may not share a wheel turn (D-040), so a choice that would is refused: nothing is saved, the
    // card that was changed says why (the other card's older note is about a different choice), and its box goes
    // back to what is stored. A choice that sticks clears both notes.
    private void ChangeWheel(Func<AppSettings, AppSettings> change, bool volume, Action putBack)
    {
        var next = change(_store.Current);
        if (WheelRules.Conflict(next.WheelVolumeArea, next.WheelVolumeModifier, next.WheelBrightnessArea, next.WheelBrightnessModifier))
        {
            WheelVolumeNote = volume ? ClashNote("Brightness", next.WheelBrightnessArea, next.WheelBrightnessModifier) : null;
            WheelBrightnessNote = volume ? null : ClashNote("Volume", next.WheelVolumeArea, next.WheelVolumeModifier);
            // A box put back from inside its own change keeps showing the refused choice: wait for it to finish.
            if (_ui?.TryEnqueue(() => putBack()) != true)
                putBack();
            return;
        }
        var before = _store.Current;
        Update(change);
        if (_store.Current != before)
            WheelVolumeNote = WheelBrightnessNote = null;
    }

    private static string ClashNote(string other, WheelArea area, WheelModifier key) =>
        $"{other} already uses the wheel {Place(area)} {Key(key)}. Pick another place or key here, or change {other} first.";

    private static string Place(WheelArea area) => area switch
    {
        WheelArea.Taskbar => "anywhere on the taskbar",
        WheelArea.Apps => "over the apps",
        _ => "over the tray icons and clock",
    };

    private static string Key(WheelModifier key) => key switch
    {
        WheelModifier.Ctrl => "while you hold Ctrl",
        WheelModifier.Shift => "while you hold Shift",
        WheelModifier.Alt => "while you hold Alt",
        _ => "without a key",
    };

    /// <summary>The screen the brightness wheel works on, or what stands in for it.</summary>
    [ObservableProperty]
    public partial string BrightnessScreen { get; set; }

    /// <summary>Why the brightness wheel can't work here, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBrightnessScreenNote))]
    public partial string? BrightnessScreenNote { get; set; }

    public bool HasBrightnessScreenNote => BrightnessScreenNote is not null;

    // Raised on the brightness thread.
    private void OnBrightnessScreenChanged(object? sender, EventArgs e) =>
        _ui?.TryEnqueue(() => (BrightnessScreen, BrightnessScreenNote) = ScreenState());

    private (string Screen, string? Note) ScreenState()
    {
        if (_brightness is null)
            return ("Not available", "Brightness couldn't start. Restart the app, or see the log.");
        if (!_brightness.Probed)
            return ("Looking…", null);
        return _brightness.ScreenName is { } name
            ? (name, null)
            : ("None found", "No built-in screen the wheel can change was found, so it does nothing here. If this PC has one, the log says why.");
    }

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
