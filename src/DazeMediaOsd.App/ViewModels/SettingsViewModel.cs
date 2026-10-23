using CommunityToolkit.Mvvm.ComponentModel;
using DazeMediaOsd.Core.Settings;

namespace DazeMediaOsd.App.ViewModels;

/// <summary>Two-way view of <see cref="SettingsStore"/>: every edit is saved at once, no Apply button.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsStore _store;

    public SettingsViewModel(SettingsStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        var current = store.Current;
        // Each setter saves back the value it just read: Update sees no change and does nothing.
        PositionIndex = (int)current.Position;
        HideAfterSeconds = current.HideAfterSeconds;
        ShowInput = current.ShowInput;
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

    /// <summary>Set while the last edit couldn't be written to disk: it applies only until the app exits.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSaveError))]
    public partial string? SaveError { get; set; }

    public bool HasSaveError => SaveError is not null;

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

    private void Update(Func<AppSettings, AppSettings> change)
    {
        _store.Update(change);
        SaveError = _store.SaveError;
    }
}
