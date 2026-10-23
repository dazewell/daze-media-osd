using DazeMediaOsd.Core.Audio;

namespace DazeMediaOsd.Core.Tests;

public class DeviceCycleTests
{
    private static readonly AudioEndpoint Dell = new("d", "DELL S2722QC (HD Audio Driver for Display Audio)", DeviceKind.Display);
    private static readonly AudioEndpoint Headphones = new("h", "Headphones (Technics EAH-AZ80)", DeviceKind.Headphones, Bluetooth: true);
    private static readonly AudioEndpoint HandsFree = new("f", "Headset (Technics EAH-AZ80 Hands-Free)", DeviceKind.Headset, Bluetooth: true);
    private static readonly AudioEndpoint Speakers = new("s", "Speakers (Realtek(R) Audio)", DeviceKind.Speakers);

    [Theory]
    [InlineData("d", "h")]
    [InlineData("h", "s")]
    [InlineData("s", "d")] // wraps around
    public void PicksTheNextByName(string current, string next) =>
        Assert.Equal(next, DeviceCycle.Next([Speakers, Headphones, Dell], current)?.Id);

    [Theory]
    [InlineData(null)]
    [InlineData("gone")]
    public void StartsAtTheFirstWhenTheDefaultIsNotListed(string? current) =>
        Assert.Equal("d", DeviceCycle.Next([Speakers, Headphones, Dell], current)?.Id);

    [Fact]
    public void SkipsTheHandsFreeOutput() =>
        Assert.Equal("s", DeviceCycle.Next([HandsFree, Speakers, Headphones], "h")?.Id);

    [Fact]
    public void LeavesTheHandsFreeOutputWhenItIsTheDefault() =>
        Assert.Equal("s", DeviceCycle.Next([HandsFree, Speakers, Headphones], "f")?.Id);

    [Fact]
    public void NothingWhenTheDefaultIsTheOnlyOne() =>
        Assert.Null(DeviceCycle.Next([Speakers], "s"));

    [Fact]
    public void NothingWithoutEndpoints() =>
        Assert.Null(DeviceCycle.Next([], null));

    [Fact]
    public void TheOnlyOneWhenThereIsNoDefault() =>
        Assert.Equal("s", DeviceCycle.Next([Speakers], null)?.Id);
}
