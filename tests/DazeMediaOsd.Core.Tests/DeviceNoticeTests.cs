using DazeMediaOsd.Core.Audio;
using DazeMediaOsd.Core.Osd;

namespace DazeMediaOsd.Core.Tests;

public class DeviceNoticeTests
{
    [Theory]
    [InlineData("Headphones (WH-1000XM5)", "WH-1000XM5")]
    [InlineData("Headset (2- WH-1000XM5)", "WH-1000XM5")]
    [InlineData("Stereo Mix (Realtek(R) Audio)", "Realtek(R) Audio")]
    [InlineData("USB Speakers", "USB Speakers")]
    [InlineData("Odd ()", "Odd ()")]
    [InlineData("", "")]
    public void ProductIsTheTrailingParentheses(string name, string product) =>
        Assert.Equal(product, DeviceNotice.Product(name));

    [Fact]
    public void NoEventsNoNotice() => Assert.Null(DeviceNotice.Summarize([]));

    [Fact]
    public void BluetoothEndpointsMergeIntoTheProduct()
    {
        var notice = DeviceNotice.Summarize(
        [
            new("Headset (WH-1000XM5)", EndpointFlow.Input, true, true),
            new("Headphones (WH-1000XM5)", EndpointFlow.Output, true, true),
            new("Headset (WH-1000XM5)", EndpointFlow.Output, true, true),
        ]);

        Assert.Equal(new DeviceNotice("WH-1000XM5", EndpointFlow.Output, true, true), notice);
    }

    [Fact]
    public void SingleBluetoothEndpointShowsTheProduct() =>
        Assert.Equal("WH-1000XM5",
            DeviceNotice.Summarize([new("Headphones (WH-1000XM5)", EndpointFlow.Output, false, true)])!.Name);

    [Fact]
    public void SingleWiredEndpointKeepsItsFullName() =>
        Assert.Equal(new DeviceNotice("Stereo Mix (Realtek(R) Audio)", EndpointFlow.Input, true, false),
            DeviceNotice.Summarize([new("Stereo Mix (Realtek(R) Audio)", EndpointFlow.Input, true, false)]));

    [Fact]
    public void UsbHeadsetEndpointsMergeIntoTheProduct() =>
        Assert.Equal(new DeviceNotice("Jabra Evolve 65", EndpointFlow.Output, true, false),
            DeviceNotice.Summarize(
            [
                new("Headset Microphone (Jabra Evolve 65)", EndpointFlow.Input, true, false),
                new("Headset Earphone (Jabra Evolve 65)", EndpointFlow.Output, true, false),
            ]));

    [Fact]
    public void LatestProductIsTheHeadlineOthersAreCounted()
    {
        var notice = DeviceNotice.Summarize(
        [
            new("Headphones (WH-1000XM5)", EndpointFlow.Output, true, true),
            new("USB Speakers", EndpointFlow.Output, false, false),
            new("Headset (WH-1000XM5)", EndpointFlow.Input, true, true),
        ]);

        Assert.Equal(new DeviceNotice("WH-1000XM5", EndpointFlow.Output, true, true, Others: 1), notice);
    }

    [Fact]
    public void LastEventOfTheProductDecidesConnected() =>
        Assert.False(DeviceNotice.Summarize(
        [
            new("Headphones (WH-1000XM5)", EndpointFlow.Output, true, true),
            new("Headphones (WH-1000XM5)", EndpointFlow.Output, false, true),
        ])!.Connected);
}
