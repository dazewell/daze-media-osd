using DazeMediaOsd.Core.Audio;

namespace DazeMediaOsd.Core.Osd;

/// <summary>Which audio changes pop the OSD.</summary>
public static class OsdRules
{
    public static OsdReason? ReasonToShow(AudioChangedEventArgs change)
    {
        ArgumentNullException.ThrowIfNull(change);
        var before = change.Previous.Get(change.Flow);
        var after = change.Current.Get(change.Flow);
        return (change.Kind, change.Flow) switch
        {
            (AudioChangeKind.DefaultDevice, EndpointFlow.Output) => OsdReason.OutputDeviceChanged,
            (AudioChangeKind.DefaultDevice, EndpointFlow.Input) => OsdReason.InputDeviceChanged,
            (AudioChangeKind.VolumeOrMute, EndpointFlow.Output) when before?.Muted != after?.Muted => OsdReason.MuteChanged,
            // One key press raises ~2 notifications with slightly different floats: compare what is displayed.
            (AudioChangeKind.VolumeOrMute, EndpointFlow.Output) when before?.Percent != after?.Percent => OsdReason.VolumeChanged,
            // Mic level alone never shows: apps with auto-gain move it constantly.
            (AudioChangeKind.VolumeOrMute, EndpointFlow.Input) when before?.Muted != after?.Muted => OsdReason.InputMuteChanged,
            _ => null,
        };
    }
}
