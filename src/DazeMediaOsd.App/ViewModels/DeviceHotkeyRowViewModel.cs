using CommunityToolkit.Mvvm.ComponentModel;
using DazeMediaOsd.Core.Audio;
using DazeMediaOsd.Core.Settings;

namespace DazeMediaOsd.App.ViewModels;

/// <summary>A device a shortcut can pick. Inactive only for a row's own device while it is unplugged.</summary>
public sealed record DeviceOption(string Id, EndpointFlow Flow, string Name, bool Active)
{
    public string Glyph => Flow == EndpointFlow.Output ? "" : ""; // Volume / Microphone

    // The glyph already says output or input; the name needs the room.
    public string Status => Active ? "" : "Not connected";

    // UI Automation reads a ComboBox item's name from it.
    public override string ToString() => $"{Name}, {(Flow == EndpointFlow.Output ? "output" : "input")}{(Active ? "" : ", not connected")}";
}

/// <summary>One device shortcut on the Shortcuts page (D-022). Every edit saves all rows through the parent.</summary>
public sealed partial class DeviceHotkeyRowViewModel : ObservableObject
{
    private readonly SettingsViewModel _parent;
    private readonly DeviceHotkey _stored; // keeps an unplugged device's name and flow
    private bool _loading;

    internal DeviceHotkeyRowViewModel(SettingsViewModel parent, DeviceHotkey stored)
    {
        _parent = parent;
        _stored = stored;
        Hotkey = stored.Hotkey;
        RefreshOptions();
    }

    /// <summary>Active devices, plus this row's own one while it isn't connected.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<DeviceOption> Options { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShortcutName))]
    public partial DeviceOption? Selected { get; set; }

    /// <summary>The recorder's UI Automation name: which device it edits, since every row has one.</summary>
    public string ShortcutName => Selected is { } device ? device.Name + " shortcut" : "Device shortcut";

    [ObservableProperty]
    public partial string Hotkey { get; set; }

    /// <summary>Why the keys pressed don't work: not a shortcut, taken by another row, or refused by Windows.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNote))]
    public partial string? Note { get; set; }

    public bool HasNote => Note is not null;

    internal void RefreshOptions()
    {
        var id = Selected?.Id ?? _stored.Id;
        var options = _parent.Choices(id.Length == 0 ? null : id);
        var selected = options.FirstOrDefault(o => o.Id == id);
        if (selected is null && id.Length > 0)
        {
            selected = Selected ?? new DeviceOption(id, _stored.Flow, _stored.Name.Length == 0 ? "Unknown device" : _stored.Name, Active: false);
            selected = selected with { Active = false };
            options.Add(selected);
        }
        _loading = true;
        Options = options;
        Selected = selected;
        _loading = false;
    }

    partial void OnSelectedChanged(DeviceOption? value)
    {
        if (!_loading && value is not null)
            _parent.SaveDeviceHotkeys();
    }

    public void Record(object sender, string chord)
    {
        if (_parent.UsedBy(chord, this) is { } note)
        {
            Note = note;
            return;
        }
        Hotkey = chord;
        _parent.SaveDeviceHotkeys();
    }

    public void Reject() => Note = SettingsViewModel.InvalidHotkeyNote;

    public void Recording(object sender, bool recording) => _parent.Recording(sender, recording);

    public void Remove() => _parent.Remove(this);

    internal DeviceHotkey ToModel() =>
        Selected is { } device ? new DeviceHotkey(device.Id, device.Flow, device.Name, Hotkey) : _stored with { Hotkey = Hotkey };

    // A row without a device or keys isn't registered, so Windows can't have refused it.
    internal string? StatusNote(string? registration) => Selected is null || Hotkey.Length == 0 ? null : registration;
}
