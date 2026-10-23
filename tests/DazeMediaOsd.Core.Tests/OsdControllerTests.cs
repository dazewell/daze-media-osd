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

    [Fact]
    public void DeviceBurstShowsOnceAfterSettlingWithCurrentDefaults()
    {
        var headset = new EndpointState("out-2", "Headset", 0.4f, false);
        using var audio = new FakeAudio { Current = new AudioSnapshot(Speakers, Mic) };
        using var keys = new FakeKeys();
        var time = new ManualTime();
        using var controller = new OsdController(audio, keys, new FakeMedia(), time);
        var shown = Record(controller);

        // A Bluetooth headset: two endpoints come up.
        audio.Change(audio.Current, EndpointFlow.Output, AudioChangeKind.DeviceConnected);
        time.Advance(TimeSpan.FromMilliseconds(300));
        audio.Change(audio.Current, EndpointFlow.Input, AudioChangeKind.DeviceConnected);
        time.Advance(TimeSpan.FromMilliseconds(300));
        Assert.Empty(shown); // still settling: the second one restarted the wait
        audio.Current = new AudioSnapshot(headset, Mic); // the show reads the state when it fires
        time.Advance(OsdController.DeviceSettle);

        Assert.Equal([new OsdState(headset, Mic, null, OsdReason.DeviceConnected)], shown);
    }

    [Fact]
    public void DefaultSwitchDuringSettleShowsOnlyOnce()
    {
        var headset = new EndpointState("out-2", "Headset", 0.4f, false);
        using var audio = new FakeAudio { Current = new AudioSnapshot(Speakers, Mic) };
        using var keys = new FakeKeys();
        var time = new ManualTime();
        using var controller = new OsdController(audio, keys, new FakeMedia(), time);
        var shown = Record(controller);

        audio.Change(audio.Current, EndpointFlow.Output, AudioChangeKind.DeviceConnected);
        audio.Change(new AudioSnapshot(headset, Mic), EndpointFlow.Output, AudioChangeKind.DefaultDevice);
        time.Advance(OsdController.DeviceSettle * 2);

        Assert.Equal([new OsdState(headset, Mic, null, OsdReason.OutputDeviceChanged)], shown);
    }

    [Fact]
    public void CallbackQueuedBeforeADefaultSwitchShowsNothing()
    {
        var headset = new EndpointState("out-2", "Headset", 0.4f, false);
        using var audio = new FakeAudio { Current = new AudioSnapshot(Speakers, Mic) };
        using var keys = new FakeKeys();
        var time = new ManualTime();
        using var controller = new OsdController(audio, keys, new FakeMedia(), time);
        var shown = Record(controller);

        audio.Change(audio.Current, EndpointFlow.Output, AudioChangeKind.DeviceConnected);
        audio.Change(new AudioSnapshot(headset, Mic), EndpointFlow.Output, AudioChangeKind.DefaultDevice);
        time.Advance(OsdController.DeviceSettle);
        time.RunAllCallbacks(); // the pool ran the settled callback late, after the cancel

        Assert.Equal([OsdReason.OutputDeviceChanged], shown.Select(s => s.Reason));
    }

    [Fact]
    public void StaleCallbackAfterReArmWaitsForTheBurst()
    {
        using var audio = new FakeAudio { Current = new AudioSnapshot(Speakers, Mic) };
        using var keys = new FakeKeys();
        var time = new ManualTime();
        using var controller = new OsdController(audio, keys, new FakeMedia(), time);
        var shown = Record(controller);

        audio.Change(audio.Current, EndpointFlow.Output, AudioChangeKind.DeviceConnected);
        time.Advance(TimeSpan.FromMilliseconds(400));
        audio.Change(audio.Current, EndpointFlow.Input, AudioChangeKind.DeviceConnected);
        time.Advance(TimeSpan.FromMilliseconds(100));
        time.RunAllCallbacks(); // the first arming's callback, queued just before the re-arm
        Assert.Empty(shown);
        time.Advance(TimeSpan.FromMilliseconds(400));

        Assert.Single(shown);
    }

    [Fact]
    public void DisconnectShowsDisconnected()
    {
        using var audio = new FakeAudio { Current = new AudioSnapshot(Speakers, Mic) };
        using var keys = new FakeKeys();
        var time = new ManualTime();
        using var controller = new OsdController(audio, keys, new FakeMedia(), time);
        var shown = Record(controller);

        audio.Change(audio.Current, EndpointFlow.Input, AudioChangeKind.DeviceDisconnected);
        time.Advance(OsdController.DeviceSettle);

        Assert.Equal([new OsdState(Speakers, Mic, null, OsdReason.DeviceDisconnected)], shown);
    }

    [Fact]
    public void DisposeCancelsPendingDeviceShow()
    {
        using var audio = new FakeAudio { Current = new AudioSnapshot(Speakers, Mic) };
        using var keys = new FakeKeys();
        var time = new ManualTime();
        var controller = new OsdController(audio, keys, new FakeMedia(), time);
        var shown = Record(controller);

        audio.Change(audio.Current, EndpointFlow.Output, AudioChangeKind.DeviceConnected);
        controller.Dispose();
        time.Advance(OsdController.DeviceSettle);
        time.RunAllCallbacks(); // one already queued when Dispose ran

        Assert.Empty(shown);
    }

    [Fact]
    public void BluetoothBurstShowsOneNamedDevice()
    {
        var headphones = new EndpointState("out-2", "Headphones (WH-1000XM5)", 0.4f, false);
        using var audio = new FakeAudio { Current = new AudioSnapshot(Speakers, Mic) };
        using var keys = new FakeKeys();
        var time = new ManualTime();
        using var controller = new OsdController(audio, keys, new FakeMedia(), time);
        var shown = Record(controller);

        audio.Device(new DeviceEvent("Headphones (WH-1000XM5)", EndpointFlow.Output, true, true));
        audio.Device(new DeviceEvent("Headset (WH-1000XM5)", EndpointFlow.Input, true, true));
        audio.Current = new AudioSnapshot(headphones, Mic);
        time.Advance(OsdController.DeviceSettle);

        var notice = new DeviceNotice("WH-1000XM5", EndpointFlow.Output, Connected: true, Bluetooth: true);
        Assert.Equal([new OsdState(headphones, Mic, null, OsdReason.DeviceConnected, notice)], shown);
    }

    [Fact]
    public void DefaultSwitchDuringSettleCarriesTheDevice()
    {
        var headphones = new EndpointState("out-2", "Headphones (WH-1000XM5)", 0.4f, false);
        using var audio = new FakeAudio { Current = new AudioSnapshot(Speakers, Mic) };
        using var keys = new FakeKeys();
        var time = new ManualTime();
        using var controller = new OsdController(audio, keys, new FakeMedia(), time);
        var shown = Record(controller);

        audio.Device(new DeviceEvent("Headphones (WH-1000XM5)", EndpointFlow.Output, true, true));
        audio.Change(new AudioSnapshot(headphones, Mic), EndpointFlow.Output, AudioChangeKind.DefaultDevice);
        time.Advance(OsdController.DeviceSettle * 2);

        var notice = new DeviceNotice("WH-1000XM5", EndpointFlow.Output, Connected: true, Bluetooth: true);
        Assert.Equal([new OsdState(headphones, Mic, null, OsdReason.OutputDeviceChanged, notice)], shown);
    }

    [Fact]
    public void DefaultSwitchWithoutPendingDeviceHasNoNotice()
    {
        var headphones = new EndpointState("out-2", "Headphones", 0.4f, false);
        using var audio = new FakeAudio { Current = new AudioSnapshot(Speakers, Mic) };
        using var keys = new FakeKeys();
        var time = new ManualTime();
        using var controller = new OsdController(audio, keys, new FakeMedia(), time);
        var shown = Record(controller);

        audio.Device(new DeviceEvent("Stereo Mix (Realtek(R) Audio)", EndpointFlow.Input, true, false));
        time.Advance(OsdController.DeviceSettle); // shown with the notice; the burst is over
        audio.Change(new AudioSnapshot(headphones, Mic), EndpointFlow.Output, AudioChangeKind.DefaultDevice);
        keys.Press(MediaKey.VolumeUp);

        Assert.Equal(["Stereo Mix (Realtek(R) Audio)", null, null], shown.Select(s => s.Device?.Name));
    }

    [Fact]
    public void NextBurstStartsEmpty()
    {
        using var audio = new FakeAudio { Current = new AudioSnapshot(Speakers, Mic) };
        using var keys = new FakeKeys();
        var time = new ManualTime();
        using var controller = new OsdController(audio, keys, new FakeMedia(), time);
        var shown = Record(controller);

        audio.Device(new DeviceEvent("Stereo Mix (Realtek(R) Audio)", EndpointFlow.Input, true, false));
        time.Advance(OsdController.DeviceSettle);
        audio.Device(new DeviceEvent("Stereo Mix (Realtek(R) Audio)", EndpointFlow.Input, false, false));
        time.Advance(OsdController.DeviceSettle);

        Assert.Equal(
            [new DeviceNotice("Stereo Mix (Realtek(R) Audio)", EndpointFlow.Input, true, false),
             new DeviceNotice("Stereo Mix (Realtek(R) Audio)", EndpointFlow.Input, false, false)],
            shown.Select(s => s.Device));
    }

    // Real Technics EAH-AZ80 connect (log, 2026-10-22): headphones, default switch 41 ms later, hands-free
    // endpoint 33 ms after that. One show, announcing the device with the new default.
    [Fact]
    public void SecondEndpointAfterDefaultSwitchDoesNotRepeat()
    {
        var headphones = new EndpointState("out-2", "Headphones (Technics EAH-AZ80)", 0.34f, false);
        using var audio = new FakeAudio { Current = new AudioSnapshot(Speakers, Mic) };
        using var keys = new FakeKeys();
        var time = new ManualTime();
        using var controller = new OsdController(audio, keys, new FakeMedia(), time);
        var shown = Record(controller);

        audio.Device(new DeviceEvent("Headphones (Technics EAH-AZ80)", EndpointFlow.Output, true, true));
        time.Advance(TimeSpan.FromMilliseconds(41));
        audio.Change(new AudioSnapshot(headphones, Mic), EndpointFlow.Output, AudioChangeKind.DefaultDevice);
        time.Advance(TimeSpan.FromMilliseconds(33));
        audio.Device(new DeviceEvent("Headset (Technics EAH-AZ80)", EndpointFlow.Input, true, true));
        time.Advance(OsdController.DeviceSettle * 2);

        var notice = new DeviceNotice("Technics EAH-AZ80", EndpointFlow.Output, true, true);
        Assert.Equal([new OsdState(headphones, Mic, null, OsdReason.OutputDeviceChanged, notice)], shown);
    }

    // Real disconnect: hands-free endpoint 930 ms after the headphones, i.e. after their settled show.
    [Fact]
    public void LateSecondEndpointOfASettledBurstDoesNotRepeat()
    {
        using var audio = new FakeAudio { Current = new AudioSnapshot(Speakers, Mic) };
        using var keys = new FakeKeys();
        var time = new ManualTime();
        using var controller = new OsdController(audio, keys, new FakeMedia(), time);
        var shown = Record(controller);

        audio.Device(new DeviceEvent("Headphones (Technics EAH-AZ80)", EndpointFlow.Output, false, true));
        time.Advance(TimeSpan.FromMilliseconds(930));
        audio.Device(new DeviceEvent("Headset (Technics EAH-AZ80)", EndpointFlow.Input, false, true));
        time.Advance(OsdController.DeviceSettle * 2);

        Assert.Equal([new DeviceNotice("Technics EAH-AZ80", EndpointFlow.Output, false, true)], shown.Select(s => s.Device));
    }

    // Real disconnect (log, 2026-10-23): default switch 29 ms after the headphones, hands-free endpoint
    // 850 ms after that, well past DeviceSettle from the switch's announce. One show.
    [Fact]
    public void LateSecondEndpointAfterDefaultSwitchDoesNotRepeat()
    {
        var headphones = new EndpointState("out-2", "Headphones (Technics EAH-AZ80)", 0.34f, false);
        using var audio = new FakeAudio { Current = new AudioSnapshot(headphones, Mic) };
        using var keys = new FakeKeys();
        var time = new ManualTime();
        using var controller = new OsdController(audio, keys, new FakeMedia(), time);
        var shown = Record(controller);

        audio.Device(new DeviceEvent("Headphones (Technics EAH-AZ80)", EndpointFlow.Output, false, true));
        time.Advance(TimeSpan.FromMilliseconds(29));
        audio.Change(new AudioSnapshot(Speakers, Mic), EndpointFlow.Output, AudioChangeKind.DefaultDevice);
        time.Advance(TimeSpan.FromMilliseconds(850));
        audio.Device(new DeviceEvent("Headset (Technics EAH-AZ80)", EndpointFlow.Input, false, true));
        time.Advance(OsdController.DeviceSettle * 2);

        var notice = new DeviceNotice("Technics EAH-AZ80", EndpointFlow.Output, false, true);
        Assert.Equal([new OsdState(Speakers, Mic, null, OsdReason.OutputDeviceChanged, notice)], shown);
    }

    [Fact]
    public void FlapWithinRepeatWindowShows()
    {
        using var audio = new FakeAudio { Current = new AudioSnapshot(Speakers, Mic) };
        using var keys = new FakeKeys();
        var time = new ManualTime();
        using var controller = new OsdController(audio, keys, new FakeMedia(), time);
        var shown = Record(controller);

        audio.Device(new DeviceEvent("Headphones (Technics EAH-AZ80)", EndpointFlow.Output, true, true));
        time.Advance(OsdController.DeviceSettle); // announced connected
        audio.Device(new DeviceEvent("Headphones (Technics EAH-AZ80)", EndpointFlow.Output, false, true));
        time.Advance(TimeSpan.FromMilliseconds(100));
        audio.Device(new DeviceEvent("Headphones (Technics EAH-AZ80)", EndpointFlow.Output, true, true));
        time.Advance(OsdController.DeviceSettle); // one burst ending "connected", but it went and came back

        Assert.Equal([true, true], shown.Select(s => s.Device!.Connected));
    }

    [Fact]
    public void SameDeviceAgainLaterShowsAgain()
    {
        using var audio = new FakeAudio { Current = new AudioSnapshot(Speakers, Mic) };
        using var keys = new FakeKeys();
        var time = new ManualTime();
        using var controller = new OsdController(audio, keys, new FakeMedia(), time);
        var shown = Record(controller);

        audio.Device(new DeviceEvent("Headphones (Technics EAH-AZ80)", EndpointFlow.Output, true, true));
        time.Advance(OsdController.DeviceSettle); // announced
        time.Advance(OsdController.RepeatWindow); // a reconnect after the window is news again
        audio.Device(new DeviceEvent("Headphones (Technics EAH-AZ80)", EndpointFlow.Output, true, true));
        time.Advance(OsdController.DeviceSettle);
        audio.Device(new DeviceEvent("Headphones (Technics EAH-AZ80)", EndpointFlow.Output, false, true));
        time.Advance(OsdController.DeviceSettle); // the opposite direction never counts as a repeat

        Assert.Equal([true, true, false], shown.Select(s => s.Device!.Connected));
    }

    private static readonly MediaState Song = new("Spotify.exe", "Song", "Artist", true, null);

    private static List<OsdState> Record(OsdController controller)
    {
        var shown = new List<OsdState>();
        controller.ShowRequested += (_, state) => shown.Add(state);
        return shown;
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
