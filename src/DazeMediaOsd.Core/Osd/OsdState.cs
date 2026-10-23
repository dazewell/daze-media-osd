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
    /// <summary>An audio endpoint connected or disconnected, default or not (D-013).</summary>
    DeviceConnected,
    DeviceDisconnected,
    /// <summary>Play/pause, next or previous (D-011).</summary>
    MediaKey,
    /// <summary>The media session changed. Only refreshes a visible OSD, never pops it.</summary>
    MediaChanged,
    /// <summary>A setting changed: show the OSD so its effect is visible.</summary>
    SettingsPreview,
    /// <summary>
    /// A shortcut found nothing to switch: no other output to cycle to (D-020), or its device is already the
    /// default or isn't connected (D-022), or is being connected (D-023). Show the current devices as the answer.
    /// </summary>
    Hotkey,
}

/// <summary>
/// Everything the OSD window renders. Immutable: a new instance per show, the window just rebinds.
/// <paramref name="Device"/> is the device that came or went, only on the show that announces it (D-014).
/// </summary>
public sealed record OsdState(
    EndpointState? Output, EndpointState? Input, MediaState? Media, OsdReason Reason, DeviceNotice? Device = null)
{
    public static OsdState From(AudioSnapshot audio, MediaState? media, OsdReason reason)
    {
        ArgumentNullException.ThrowIfNull(audio);
        return new(audio.Output, audio.Input, media, reason);
    }

    /// <summary>One line for the log.</summary>
    public string Describe() =>
        $"{Reason}: {Describe(new AudioSnapshot(Output, Input))}{(Media is null ? "" : $" media=\"{Media.Title}\"{(Media.IsPlaying ? " playing" : "")}")}"
        + (Device is null ? "" : $" device=\"{Device.Name}\" {Device.Answer switch
        {
            PressAnswer.NotConnected => "not connected",
            PressAnswer.Connecting => "connecting",
            PressAnswer.ConnectFailed => "connect failed",
            _ => Device.Connected ? "connected" : "disconnected",
        }}"
            + $"{(Device.Bluetooth ? " bt" : "")}{(Device.Kind == DeviceKind.Other ? "" : " " + Device.Kind)}{(Device.Others > 0 ? " +" + Device.Others.ToString(CultureInfo.InvariantCulture) : "")}");

    public static string Describe(AudioSnapshot audio)
    {
        ArgumentNullException.ThrowIfNull(audio);
        return $"out={Describe(audio.Output)} in={Describe(audio.Input)}";
    }

    private static string Describe(EndpointState? endpoint) =>
        endpoint is null
            ? "none"
            : string.Create(CultureInfo.InvariantCulture,
                $"\"{endpoint.Name}\" {endpoint.Percent}{(endpoint.Muted ? " muted" : "")}{(endpoint.Kind == DeviceKind.Other ? "" : " " + endpoint.Kind)}");
}
