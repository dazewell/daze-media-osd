using DazeMediaOsd.Core.Audio;
using DazeMediaOsd.Core.Input;
using DazeMediaOsd.Core.Media;

namespace DazeMediaOsd.Core.Osd;

/// <summary>Which audio changes and keys pop the OSD (D-007, D-011, D-013).</summary>
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
            (AudioChangeKind.DeviceConnected, _) => OsdReason.DeviceConnected,
            (AudioChangeKind.DeviceDisconnected, _) => OsdReason.DeviceDisconnected,
            (AudioChangeKind.VolumeOrMute, EndpointFlow.Output) when before?.Muted != after?.Muted => OsdReason.MuteChanged,
            // One key press raises ~2 notifications with slightly different floats: compare what is displayed.
            (AudioChangeKind.VolumeOrMute, EndpointFlow.Output) when before?.Percent != after?.Percent => OsdReason.VolumeChanged,
            // Mic level alone never shows: apps with auto-gain move it constantly.
            (AudioChangeKind.VolumeOrMute, EndpointFlow.Input) when before?.Muted != after?.Muted => OsdReason.InputMuteChanged,
            _ => null,
        };
    }

    /// <summary>
    /// Volume keys always show (at 0 %/100 % no volume change follows). Media keys show only while a
    /// session exists: with nothing playing there's nothing to show, like the native flyout (D-011).
    /// </summary>
    public static OsdReason? ReasonToShow(MediaKey key, MediaState? media) => key switch
    {
        MediaKey.VolumeUp or MediaKey.VolumeDown or MediaKey.Mute => OsdReason.VolumeKey,
        MediaKey.PlayPause or MediaKey.NextTrack or MediaKey.PreviousTrack when media is not null => OsdReason.MediaKey,
        _ => null,
    };

    /// <summary>
    /// A new track, or play/pause, in the same player; or a player that starts playing. A cover arriving late,
    /// a session that ends or one that appears paused only refresh (D-032). The app drops it unless the
    /// user turned it on.
    /// </summary>
    public static OsdReason? ReasonToShow(MediaState? before, MediaState? after)
    {
        if (after is null)
            return null;
        if (before is null || before.AppId != after.AppId)
            return after.IsPlaying ? OsdReason.MediaPlayback : null;
        var newTrack = before.Title != after.Title || before.Artist != after.Artist;
        return newTrack || before.IsPlaying != after.IsPlaying ? OsdReason.MediaPlayback : null;
    }

    /// <summary>
    /// The line that says why the OSD popped, when its rows don't (user, 2026-11-08; D-042): a call or a page
    /// that registers as a player has a title but no artist, and an app or Windows can change the volume or
    /// mute the mic without a key. <paramref name="userJustActed"/>: a volume key or wheel step came within
    /// <see cref="OsdController.UserWindow"/>, so the change is the user's own.
    /// </summary>
    public static OsdCause? CauseOf(OsdState state, bool userJustActed)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Device is not null || state.Brightness is not null)
            return null; // those say what happened themselves
        return state.Reason switch
        {
            OsdReason.MediaPlayback when state.Media is { Artist.Length: 0 } media =>
                new(OsdCauseKind.Media, media.AppName.Length == 0 ? "Media" : media.AppName, media.IsPlaying ? "Playing" : "Paused"),
            OsdReason.VolumeChanged when !userJustActed => new(OsdCauseKind.Volume, "Volume", "Changed by an app"),
            OsdReason.MuteChanged when !userJustActed =>
                new(OsdCauseKind.Volume, "Sound", state.Output is { Muted: true } ? "Muted by an app" : "Unmuted by an app"),
            OsdReason.InputMuteChanged =>
                new(OsdCauseKind.Microphone, "Microphone", state.Input is { Muted: true } ? "Muted" : "Unmuted"),
            _ => null,
        };
    }
}
