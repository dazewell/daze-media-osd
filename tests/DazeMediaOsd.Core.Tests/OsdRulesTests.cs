using DazeMediaOsd.Core.Audio;
using DazeMediaOsd.Core.Input;
using DazeMediaOsd.Core.Media;
using DazeMediaOsd.Core.Osd;

namespace DazeMediaOsd.Core.Tests;

public class OsdRulesTests
{
    private static readonly EndpointState Speakers = new("out-1", "Speakers", 0.5f, false);
    private static readonly EndpointState Headphones = new("out-2", "Headphones", 0.3f, false);
    private static readonly EndpointState Mic = new("in-1", "Microphone", 0.8f, false);
    private static readonly AudioSnapshot Before = new(Speakers, Mic);

    private static OsdReason? Reason(AudioSnapshot after, EndpointFlow flow, AudioChangeKind kind) =>
        OsdRules.ReasonToShow(new AudioChangedEventArgs(Before, after, flow, kind));

    [Fact]
    public void OutputVolumeChangeShows() =>
        Assert.Equal(OsdReason.VolumeChanged,
            Reason(Before with { Output = Speakers with { Volume = 0.52f } }, EndpointFlow.Output, AudioChangeKind.VolumeOrMute));

    [Fact]
    public void VolumeJitterBelowOnePercentDoesNotShow() =>
        Assert.Null(Reason(Before with { Output = Speakers with { Volume = 0.5001f } }, EndpointFlow.Output, AudioChangeKind.VolumeOrMute));

    [Fact]
    public void OutputMuteWinsOverVolume() =>
        Assert.Equal(OsdReason.MuteChanged,
            Reason(Before with { Output = Speakers with { Muted = true, Volume = 0.6f } }, EndpointFlow.Output, AudioChangeKind.VolumeOrMute));

    [Fact]
    public void MicLevelAloneDoesNotShow() =>
        Assert.Null(Reason(Before with { Input = Mic with { Volume = 0.4f } }, EndpointFlow.Input, AudioChangeKind.VolumeOrMute));

    [Fact]
    public void MicMuteShows() =>
        Assert.Equal(OsdReason.InputMuteChanged,
            Reason(Before with { Input = Mic with { Muted = true } }, EndpointFlow.Input, AudioChangeKind.VolumeOrMute));

    [Fact]
    public void DefaultOutputChangeShows() =>
        Assert.Equal(OsdReason.OutputDeviceChanged,
            Reason(Before with { Output = Headphones }, EndpointFlow.Output, AudioChangeKind.DefaultDevice));

    [Fact]
    public void LosingTheLastMicShows() =>
        Assert.Equal(OsdReason.InputDeviceChanged, Reason(Before with { Input = null }, EndpointFlow.Input, AudioChangeKind.DefaultDevice));

    [Theory]
    [InlineData(AudioChangeKind.DeviceConnected, OsdReason.DeviceConnected)]
    [InlineData(AudioChangeKind.DeviceDisconnected, OsdReason.DeviceDisconnected)]
    public void DeviceConnectOrDisconnectShowsDefaultOrNot(AudioChangeKind kind, OsdReason expected) =>
        Assert.Equal(expected, Reason(Before, EndpointFlow.Input, kind));

    [Fact]
    public void RenameDoesNotShow() =>
        Assert.Null(Reason(Before with { Output = Speakers with { Name = "Desk speakers" } }, EndpointFlow.Output, AudioChangeKind.DeviceRenamed));

    [Theory]
    [InlineData(MediaKey.VolumeUp)]
    [InlineData(MediaKey.VolumeDown)]
    [InlineData(MediaKey.Mute)]
    public void VolumeKeysShowWithoutMedia(MediaKey key) =>
        Assert.Equal(OsdReason.VolumeKey, OsdRules.ReasonToShow(key, null));

    [Theory]
    [InlineData(MediaKey.PlayPause)]
    [InlineData(MediaKey.NextTrack)]
    [InlineData(MediaKey.PreviousTrack)]
    public void MediaKeysShowOnlyWithASession(MediaKey key)
    {
        Assert.Equal(OsdReason.MediaKey, OsdRules.ReasonToShow(key, new MediaState("app", "Song", "", false, null)));
        Assert.Null(OsdRules.ReasonToShow(key, null));
    }

    private static readonly MediaState Playing = new("Spotify.exe", "Song", "Artist", true, null);

    public static TheoryData<MediaState?, MediaState?, bool> MediaChanges => new()
    {
        { Playing, Playing with { Title = "Next" }, true },          // next track
        { Playing, Playing with { Artist = "Other" }, true },
        { Playing, Playing with { IsPlaying = false }, true },       // pause
        { Playing with { IsPlaying = false }, Playing, true },       // play
        { null, Playing, true },                                     // a player starts playing
        { Playing, Playing with { AppId = "chrome", Title = "Video" }, true },
        { null, Playing with { IsPlaying = false }, false },         // a session appears paused
        { Playing, null, false },                                    // the session ends
        { Playing, Playing with { Thumbnail = [1] }, false },        // the cover arrives late
        { Playing, Playing with { AppId = "chrome", IsPlaying = false }, false },
    };

    [Theory]
    [MemberData(nameof(MediaChanges))]
    public void MediaChangePopsOnlyForTrackOrPlayState(MediaState? before, MediaState? after, bool shows) =>
        Assert.Equal(shows ? OsdReason.MediaPlayback : null, OsdRules.ReasonToShow(before, after));
}