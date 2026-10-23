using DazeMediaOsd.Core.Audio;
using DazeMediaOsd.Core.Input;
using DazeMediaOsd.Core.Osd;

namespace DazeMediaOsd.Core.Tests;

public class OsdControllerTests
{
    private static readonly EndpointState Speakers = new("out-1", "Speakers", 1f, false);
    private static readonly EndpointState Mic = new("in-1", "Microphone", 0.8f, false);

    [Fact]
    public void VolumeKeyAtMaximumShowsCurrentState()
    {
        using var audio = new FakeAudio { Current = new AudioSnapshot(Speakers, Mic) };
        using var keys = new FakeKeys();
        using var controller = new OsdController(audio, keys);
        var shown = Record(controller);

        keys.Press(VolumeKey.Up); // at the maximum no volume change follows

        Assert.Equal([new OsdState(Speakers, Mic, OsdReason.VolumeKey)], shown);
    }

    [Fact]
    public void DefaultDeviceChangeShowsNewDevice()
    {
        var headphones = new EndpointState("out-2", "Headphones", 0.3f, false);
        using var audio = new FakeAudio { Current = new AudioSnapshot(Speakers, Mic) };
        using var keys = new FakeKeys();
        using var controller = new OsdController(audio, keys);
        var shown = Record(controller);

        audio.Change(new AudioSnapshot(headphones, Mic), EndpointFlow.Output, AudioChangeKind.DefaultDevice);

        Assert.Equal([new OsdState(headphones, Mic, OsdReason.OutputDeviceChanged)], shown);
    }

    [Fact]
    public void IgnoredChangeShowsNothing()
    {
        using var audio = new FakeAudio { Current = new AudioSnapshot(Speakers, Mic) };
        using var keys = new FakeKeys();
        using var controller = new OsdController(audio, keys);
        var shown = Record(controller);

        audio.Change(new AudioSnapshot(Speakers, Mic with { Volume = 0.2f }), EndpointFlow.Input, AudioChangeKind.VolumeOrMute);

        Assert.Empty(shown);
    }

    [Fact]
    public void DisposeUnsubscribes()
    {
        using var audio = new FakeAudio { Current = new AudioSnapshot(Speakers, Mic) };
        using var keys = new FakeKeys();
        var controller = new OsdController(audio, keys);
        var shown = Record(controller);

        controller.Dispose();
        keys.Press(VolumeKey.Mute);

        Assert.Empty(shown);
    }

    private static List<OsdState> Record(OsdController controller)
    {
        var shown = new List<OsdState>();
        controller.ShowRequested += (_, state) => shown.Add(state);
        return shown;
    }

    private sealed class FakeAudio : IAudioService
    {
        public AudioSnapshot Current { get; set; } = AudioSnapshot.Empty;

        public event EventHandler<AudioChangedEventArgs>? Changed;

        public void Change(AudioSnapshot next, EndpointFlow flow, AudioChangeKind kind)
        {
            var previous = Current;
            Current = next;
            Changed?.Invoke(this, new AudioChangedEventArgs(previous, next, flow, kind));
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeKeys : IKeySource
    {
        public event EventHandler<VolumeKeyEventArgs>? KeyPressed;

        public void Press(VolumeKey key) => KeyPressed?.Invoke(this, new VolumeKeyEventArgs(key));

        public void Dispose()
        {
        }
    }
}
