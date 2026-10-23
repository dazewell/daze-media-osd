using DazeMediaOsd.Core.Audio;
using DazeMediaOsd.Core.Input;
using DazeMediaOsd.Core.Media;
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
        using var controller = new OsdController(audio, keys, new FakeMedia());
        var shown = Record(controller);

        keys.Press(MediaKey.VolumeUp); // at the maximum no volume change follows

        Assert.Equal([new OsdState(Speakers, Mic, null, OsdReason.VolumeKey)], shown);
    }

    [Fact]
    public void DefaultDeviceChangeShowsNewDevice()
    {
        var headphones = new EndpointState("out-2", "Headphones", 0.3f, false);
        using var audio = new FakeAudio { Current = new AudioSnapshot(Speakers, Mic) };
        using var keys = new FakeKeys();
        using var controller = new OsdController(audio, keys, new FakeMedia());
        var shown = Record(controller);

        audio.Change(new AudioSnapshot(headphones, Mic), EndpointFlow.Output, AudioChangeKind.DefaultDevice);

        Assert.Equal([new OsdState(headphones, Mic, null, OsdReason.OutputDeviceChanged)], shown);
    }

    [Fact]
    public void IgnoredChangeShowsNothing()
    {
        using var audio = new FakeAudio { Current = new AudioSnapshot(Speakers, Mic) };
        using var keys = new FakeKeys();
        using var controller = new OsdController(audio, keys, new FakeMedia());
        var shown = Record(controller);

        audio.Change(new AudioSnapshot(Speakers, Mic with { Volume = 0.2f }), EndpointFlow.Input, AudioChangeKind.VolumeOrMute);

        Assert.Empty(shown);
    }

    [Fact]
    public void DisposeUnsubscribes()
    {
        using var audio = new FakeAudio { Current = new AudioSnapshot(Speakers, Mic) };
        using var keys = new FakeKeys();
        var controller = new OsdController(audio, keys, new FakeMedia());
        var shown = Record(controller);

        controller.Dispose();
        keys.Press(MediaKey.Mute);

        Assert.Empty(shown);
    }

    [Fact]
    public void IdenticalRequestWithinWindowIsCoalesced()
    {
        using var audio = new FakeAudio { Current = new AudioSnapshot(Speakers, Mic) };
        using var keys = new FakeKeys();
        var time = new ManualTime();
        using var controller = new OsdController(audio, keys, new FakeMedia(), time);
        var shown = Record(controller);

        keys.Press(MediaKey.VolumeUp); // keyboard path
        time.Advance(TimeSpan.FromMilliseconds(5));
        keys.Press(MediaKey.VolumeUp); // same press via Consumer Control
        time.Advance(OsdController.CoalesceWindow);
        keys.Press(MediaKey.VolumeUp); // held key, after the window

        Assert.Equal(2, shown.Count);
    }

    [Fact]
    public void DifferentStateWithinWindowIsNotCoalesced()
    {
        using var audio = new FakeAudio { Current = new AudioSnapshot(Speakers with { Volume = 0.5f }, Mic) };
        using var keys = new FakeKeys();
        using var controller = new OsdController(audio, keys, new FakeMedia(), new ManualTime());
        var shown = Record(controller);

        keys.Press(MediaKey.VolumeUp);
        audio.Change(new AudioSnapshot(Speakers with { Volume = 0.52f }, Mic), EndpointFlow.Output, AudioChangeKind.VolumeOrMute);

        Assert.Equal([OsdReason.VolumeKey, OsdReason.VolumeChanged], shown.Select(s => s.Reason));
    }

    [Fact]
    public void MediaKeyWithSessionShowsMedia()
    {
        using var audio = new FakeAudio { Current = new AudioSnapshot(Speakers, Mic) };
        using var keys = new FakeKeys();
        using var media = new FakeMedia { Current = Song };
        using var controller = new OsdController(audio, keys, media);
        var shown = Record(controller);

        keys.Press(MediaKey.NextTrack);

        Assert.Equal([new OsdState(Speakers, Mic, Song, OsdReason.MediaKey)], shown);
    }

    [Fact]
    public void MediaKeyWithoutSessionShowsNothing()
    {
        using var audio = new FakeAudio { Current = new AudioSnapshot(Speakers, Mic) };
        using var keys = new FakeKeys();
        using var controller = new OsdController(audio, keys, new FakeMedia());
        var shown = Record(controller);

        keys.Press(MediaKey.PlayPause);

        Assert.Empty(shown);
    }

    [Fact]
    public void MediaChangeRefreshesButNeverShows()
    {
        using var audio = new FakeAudio { Current = new AudioSnapshot(Speakers, Mic) };
        using var keys = new FakeKeys();
        using var media = new FakeMedia();
        using var controller = new OsdController(audio, keys, media);
        var shown = Record(controller);
        var refreshed = new List<OsdState>();
        controller.RefreshRequested += (_, state) => refreshed.Add(state);

        media.Change(Song);

        Assert.Empty(shown);
        Assert.Equal([new OsdState(Speakers, Mic, Song, OsdReason.MediaChanged)], refreshed);
    }

    [Fact]
    public void AudioChangeCarriesCurrentMedia()
    {
        using var audio = new FakeAudio { Current = new AudioSnapshot(Speakers, Mic) };
        using var keys = new FakeKeys();
        using var controller = new OsdController(audio, keys, new FakeMedia { Current = Song });
        var shown = Record(controller);

        audio.Change(new AudioSnapshot(Speakers with { Muted = true }, Mic), EndpointFlow.Output, AudioChangeKind.VolumeOrMute);

        Assert.Equal(Song, Assert.Single(shown).Media);
    }

    private static readonly MediaState Song = new("Spotify.exe", "Song", "Artist", true, null);

    private static List<OsdState> Record(OsdController controller)
    {
        var shown = new List<OsdState>();
        controller.ShowRequested += (_, state) => shown.Add(state);
        return shown;
    }

    private sealed class ManualTime : TimeProvider
    {
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _ticks;

        public void Advance(TimeSpan by) => _ticks += by.Ticks;
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

    private sealed class FakeMedia : IMediaService
    {
        public MediaState? Current { get; set; }

        public event EventHandler<MediaState?>? Changed;

        public void Change(MediaState? next)
        {
            Current = next;
            Changed?.Invoke(this, next);
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeKeys : IKeySource
    {
        public event EventHandler<MediaKeyEventArgs>? KeyPressed;

        public void Press(MediaKey key) => KeyPressed?.Invoke(this, new MediaKeyEventArgs(key));

        public void Dispose()
        {
        }
    }
}
