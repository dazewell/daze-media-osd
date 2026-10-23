namespace DazeMediaOsd.Core.Audio;

public enum EndpointFlow
{
    Output,
    Input,
}

/// <summary>A default audio endpoint. <paramref name="Volume"/> is the 0–1 scalar Windows shows as 0–100.</summary>
public sealed record EndpointState(string Id, string Name, float Volume, bool Muted)
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
/// </summary>
public sealed record DeviceEvent(string Name, EndpointFlow Flow, bool Connected, bool Bluetooth);

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

public interface IAudioService : IDisposable
{
    AudioSnapshot Current { get; }

    /// <summary>Raised on the service's own thread; handlers must marshal before touching UI.</summary>
    event EventHandler<AudioChangedEventArgs>? Changed;
}
