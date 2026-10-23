using DazeMediaOsd.Core.Audio;

namespace DazeMediaOsd.Core.Tests;

public class DeviceChoicesTests
{
    // Names as on 26300 (2026-10-23).
    private static readonly AudioEndpoint Speakers = new("s", "Speakers (Realtek(R) Audio)", DeviceKind.Speakers);
    private static readonly AudioEndpoint Headphones = new("h", "Headphones (Technics EAH-AZ80)", DeviceKind.Headphones, Bluetooth: true);
    private static readonly AudioEndpoint HandsFree = new("f", "Headset (Technics EAH-AZ80 Hands-Free)", DeviceKind.Headset, Bluetooth: true);

    private static string[] Ids(IReadOnlyList<AudioEndpoint> endpoints) => [.. endpoints.Select(e => e.Id)];

    [Fact]
    public void SortsByName() =>
        Assert.Equal(["h", "s"], Ids(DeviceChoices.For([Speakers, Headphones], null)));

    [Fact]
    public void HidesHandsFreeOutputOfAProductWithStereoOutput() =>
        Assert.Equal(["h", "s"], Ids(DeviceChoices.For([HandsFree, Speakers, Headphones], "s")));

    [Fact]
    public void KeepsHandsFreeOutputWhenItIsTheDefault() =>
        Assert.Equal(["h", "f", "s"], Ids(DeviceChoices.For([HandsFree, Speakers, Headphones], "f")));

    [Fact]
    public void KeepsHandsFreeOutputOfAProductWithoutStereoOutput() =>
        Assert.Equal(["f", "s"], Ids(DeviceChoices.For([HandsFree, Speakers], "s")));

    [Fact]
    public void KeepsAWiredHeadsetNextToOtherHeadphones()
    {
        var usb = new AudioEndpoint("u", "Headset (Technics EAH-AZ80)", DeviceKind.Headset);
        Assert.Equal(["h", "u"], Ids(DeviceChoices.For([usb, Headphones], null)));
    }

    [Fact]
    public void MatchesTheProductOfANumberedSecondInstance()
    {
        var second = new AudioEndpoint("f2", "Headset (2- Technics EAH-AZ80 Hands-Free)", DeviceKind.Headset, Bluetooth: true);
        Assert.Equal(["h"], Ids(DeviceChoices.For([second, Headphones], null)));
    }
}
