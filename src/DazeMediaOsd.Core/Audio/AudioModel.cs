namespace DazeMediaOsd.Core.Audio;

public enum EndpointFlow
{
    Output,
    Input,
}

/// <summary>What kind of device an endpoint is, for its glyph (D-017). Other: unknown, or nothing more specific.</summary>
public enum DeviceKind
{
    Other,
    Speakers,
    Headphones,
    Headset,
    Display,
    Microphone,
}

public static class DeviceKinds
{
    /// <summary>
    /// <c>PKEY_AudioEndpoint_FormFactor</c> (an <c>EndpointFormFactor</c>) → kind. Line level, S/PDIF and
    /// passthrough are Other: they say how the device is wired, not what it is.
    /// </summary>
    public static DeviceKind FromFormFactor(uint formFactor) => formFactor switch
    {
        1 => DeviceKind.Speakers,
        3 => DeviceKind.Headphones,
        4 => DeviceKind.Microphone,
        5 or 6 => DeviceKind.Headset, // Headset, Handset
        9 => DeviceKind.Display,      // DigitalAudioDisplayDevice: HDMI / DisplayPort monitor or TV
        _ => DeviceKind.Other,
    };
}

/// <summary>A default audio endpoint. <paramref name="Volume"/> is the 0–1 scalar Windows shows as 0–100.</summary>
public sealed record EndpointState(string Id, string Name, float Volume, bool Muted, DeviceKind Kind = DeviceKind.Other)
{
    /// <summary>The 0–100 value the OSD displays.</summary>
    public int Percent => (int)MathF.Round(Volume * 100);
}

/// <summary>Default output and input endpoints (role eConsole, what Sound settings shows); null when none exists.</summary>
public sealed record AudioSnapshot(EndpointState? Output, EndpointState? Input)
{
    public static AudioSnapshot Empty { get; } = new(null, null);

    public EndpointState? Get(EndpointFlow flow) => flow == EndpointFlow.Output ? Output : Input;

    public AudioSnapshot With(EndpointFlow flow, EndpointState? state) =>
        flow == EndpointFlow.Output ? this with { Output = state } : this with { Input = state };
}

public enum AudioChangeKind
{
    VolumeOrMute,
    DefaultDevice,
    DeviceRenamed,
    /// <summary>An endpoint of this flow became active (plugged in, paired, enabled); not necessarily the default.</summary>
    DeviceConnected,
    /// <summary>An endpoint of this flow stopped being active (unplugged, disconnected, disabled, removed).</summary>
    DeviceDisconnected,
}

/// <summary>
/// One endpoint that connected or disconnected. <paramref name="Name"/> is its friendly name ("" when it
/// could no longer be read); <paramref name="Bluetooth"/> when a Bluetooth enumerator created it.
/// <paramref name="Id"/> is the endpoint id ("" when not known).
/// </summary>
public sealed record DeviceEvent(
    string Name, EndpointFlow Flow, bool Connected, bool Bluetooth, DeviceKind Kind = DeviceKind.Other, string Id = "");

public sealed class AudioChangedEventArgs(
    AudioSnapshot previous, AudioSnapshot current, EndpointFlow flow, AudioChangeKind kind, DeviceEvent? device = null)
    : EventArgs
{
    public AudioSnapshot Previous { get; } = previous;
    public AudioSnapshot Current { get; } = current;
    public EndpointFlow Flow { get; } = flow;
    public AudioChangeKind Kind { get; } = kind;
    /// <summary>The endpoint that came or went; set for DeviceConnected/DeviceDisconnected only.</summary>
    public DeviceEvent? Device { get; } = device;
}

/// <summary>An active endpoint one can switch to. <paramref name="Bluetooth"/> as in <see cref="DeviceEvent"/>.</summary>
public sealed record AudioEndpoint(string Id, string Name, DeviceKind Kind, bool Bluetooth = false);

/// <summary>What asking a paired, disconnected device to connect did (D-023).</summary>
public enum ConnectResult
{
    /// <summary>Bluetooth took the request; the endpoint turns active once the device answers, or never.</summary>
    Requested,
    AlreadyConnected,
    /// <summary>Not a paired Bluetooth endpoint: unpaired, disabled, wired, or a driver without the request.</summary>
    NotSupported,
}

public interface IAudioService : IDisposable
{
    AudioSnapshot Current { get; }

    /// <summary>The active endpoints of <paramref name="flow"/>, read now; empty if Core Audio doesn't answer.</summary>
    IReadOnlyList<AudioEndpoint> Endpoints(EndpointFlow flow);

    /// <summary>
    /// Makes <paramref name="id"/> the default <paramref name="flow"/> endpoint (D-019). Returns at once; a
    /// failure only logs, success shows up as a <see cref="AudioChangeKind.DefaultDevice"/> change.
    /// </summary>
    void SetDefault(string id, EndpointFlow flow);

    /// <summary>
    /// Asks the paired Bluetooth device behind endpoint <paramref name="id"/> to connect (D-023). Waits at most
    /// a couple of seconds; success shows up later as a <see cref="AudioChangeKind.DeviceConnected"/> change.
    /// </summary>
    ConnectResult Connect(string id);

    /// <summary>Raised on the service's own thread; handlers must marshal before touching UI.</summary>
    event EventHandler<AudioChangedEventArgs>? Changed;
}
