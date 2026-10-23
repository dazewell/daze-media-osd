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
}

public sealed class AudioChangedEventArgs(AudioSnapshot previous, AudioSnapshot current, EndpointFlow flow, AudioChangeKind kind)
    : EventArgs
{
    public AudioSnapshot Previous { get; } = previous;
    public AudioSnapshot Current { get; } = current;
    public EndpointFlow Flow { get; } = flow;
    public AudioChangeKind Kind { get; } = kind;
}

public interface IAudioService : IDisposable
{
    AudioSnapshot Current { get; }

    /// <summary>Raised on the service's own thread; handlers must marshal before touching UI.</summary>
    event EventHandler<AudioChangedEventArgs>? Changed;
}
