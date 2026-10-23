using System.Globalization;
using DazeMediaOsd.Core.Audio;
using DazeMediaOsd.Core.Media;

namespace DazeMediaOsd.Core.Osd;

/// <summary>Why the OSD is being shown; lets the window emphasise what changed.</summary>
public enum OsdReason
{
    VolumeKey,
    VolumeChanged,
    MuteChanged,
    OutputDeviceChanged,
    InputDeviceChanged,
    InputMuteChanged,
    /// <summary>Play/pause, next or previous (D-011).</summary>
    MediaKey,
    /// <summary>The media session changed. Only refreshes a visible OSD, never pops it.</summary>
    MediaChanged,
    /// <summary>A setting changed: show the OSD so its effect is visible.</summary>
    SettingsPreview,
}

/// <summary>Everything the OSD window renders. Immutable: a new instance per show, the window just rebinds.</summary>
public sealed record OsdState(EndpointState? Output, EndpointState? Input, MediaState? Media, OsdReason Reason)
{
    public static OsdState From(AudioSnapshot audio, MediaState? media, OsdReason reason)
    {
        ArgumentNullException.ThrowIfNull(audio);
        return new(audio.Output, audio.Input, media, reason);
    }

    /// <summary>One line for the log.</summary>
    public string Describe() =>
        $"{Reason}: {Describe(new AudioSnapshot(Output, Input))}{(Media is null ? "" : $" media=\"{Media.Title}\"{(Media.IsPlaying ? " playing" : "")}")}";

    public static string Describe(AudioSnapshot audio)
    {
        ArgumentNullException.ThrowIfNull(audio);
        return $"out={Describe(audio.Output)} in={Describe(audio.Input)}";
    }

    private static string Describe(EndpointState? endpoint) =>
        endpoint is null
            ? "none"
            : string.Create(CultureInfo.InvariantCulture,
                $"\"{endpoint.Name}\" {endpoint.Percent}{(endpoint.Muted ? " muted" : "")}");
}
