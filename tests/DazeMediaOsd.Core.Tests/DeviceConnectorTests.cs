using DazeMediaOsd.Core.Audio;
using DazeMediaOsd.Core.Settings;

namespace DazeMediaOsd.Core.Tests;

public class DeviceConnectorTests
{
    private static readonly DeviceHotkey Technics = new("h", EndpointFlow.Output, "Headphones (Technics EAH-AZ80)", "Win+Alt+2");
    private static readonly DeviceHotkey Sony = new("x", EndpointFlow.Output, "Headphones (WH-1000XM5)", "Win+Alt+3");

    [Fact]
    public void SwitchesOnceTheDeviceConnects()
    {
        var (audio, time, connector, timedOut) = Create();

        Assert.Equal(ConnectResult.Requested, connector.Connect(Technics));
        Assert.Empty(audio.Defaults);
        audio.Device(Connected("Headset (Technics EAH-AZ80)", "hf", EndpointFlow.Input)); // the other endpoint first
        Assert.Empty(audio.Defaults);
        audio.Device(Connected(Technics.Name, "h"));
        time.Advance(DeviceConnector.Patience);

        Assert.Equal([("h", EndpointFlow.Output)], audio.Defaults);
        Assert.Empty(timedOut);
    }

    [Fact]
    public void TimesOutWhenTheDeviceNeverConnects()
    {
        var (audio, time, connector, timedOut) = Create();

        connector.Connect(Technics);
        time.Advance(DeviceConnector.Patience - TimeSpan.FromMilliseconds(1));
        Assert.Empty(timedOut);
        time.Advance(TimeSpan.FromMilliseconds(1));
        audio.Device(Connected(Technics.Name, "h")); // too late: Windows' own switch is all that happens

        Assert.Equal([Technics], timedOut);
        Assert.Empty(audio.Defaults);
    }

    [Fact]
    public void NothingWaitsWhenTheRequestWasRefused()
    {
        var (audio, time, connector, timedOut) = Create(ConnectResult.NotSupported);

        Assert.Equal(ConnectResult.NotSupported, connector.Connect(Technics));
        audio.Device(Connected(Technics.Name, "h"));
        time.Advance(DeviceConnector.Patience);

        Assert.Empty(audio.Defaults);
        Assert.Empty(timedOut);
    }

    [Fact]
    public void AlreadyConnectedSwitchesAtOnce()
    {
        var (audio, time, connector, timedOut) = Create(ConnectResult.AlreadyConnected);

        connector.Connect(Technics);
        time.Advance(DeviceConnector.Patience);

        Assert.Equal([("h", EndpointFlow.Output)], audio.Defaults);
        Assert.Empty(timedOut);
    }

    [Fact]
    public void ConnectBeforeTheRequestReturnsStillSwitches()
    {
        var (audio, _, connector, _) = Create();
        audio.WhileConnecting = _ => audio.Device(Connected(Technics.Name, "h"));

        connector.Connect(Technics);

        Assert.Equal([("h", EndpointFlow.Output)], audio.Defaults);
    }

    [Fact]
    public void ALaterPressReplacesTheWaitingOne()
    {
        var (audio, time, connector, timedOut) = Create();

        connector.Connect(Technics);
        connector.Connect(Sony);
        audio.Device(Connected(Technics.Name, "h"));
        Assert.Empty(audio.Defaults);
        audio.Device(Connected(Sony.Name, "x"));
        time.Advance(DeviceConnector.Patience);

        Assert.Equal([("x", EndpointFlow.Output)], audio.Defaults);
        Assert.Empty(timedOut);
    }

    private static DeviceEvent Connected(string name, string id, EndpointFlow flow = EndpointFlow.Output) =>
        new(name, flow, Connected: true, Bluetooth: true, DeviceKind.Headphones, id);

    private static (FakeAudio, ManualTime, DeviceConnector, List<DeviceHotkey>) Create(
        ConnectResult answer = ConnectResult.Requested)
    {
        var audio = new FakeAudio { ConnectAnswer = answer };
        var time = new ManualTime();
        var connector = new DeviceConnector(audio, time);
        var timedOut = new List<DeviceHotkey>();
        connector.TimedOut += (_, device) => timedOut.Add(device);
        return (audio, time, connector, timedOut);
    }
}
