using DazeMediaOsd.Core.Audio;
using DazeMediaOsd.Core.Input;
using DazeMediaOsd.Core.Settings;

namespace DazeMediaOsd.Core.Tests;

public class ShortcutsTests
{
    private static readonly DeviceHotkey Speakers = new("s", EndpointFlow.Output, "Speakers (Realtek(R) Audio)", "Win+Alt+1");
    private static readonly DeviceHotkey Technics = new("h", EndpointFlow.Output, "Headphones (Technics EAH-AZ80)", "Win+Alt+2");
    private static readonly AudioEndpoint SpeakersEndpoint = new("s", "Speakers (Realtek(R) Audio)", DeviceKind.Speakers);

    [Fact]
    public void MapsEachChordToItsAction()
    {
        var settings = new AppSettings(CycleOutputHotkey: "Win+Alt+A", DeviceHotkeys: [Speakers, Technics]).Normalize();

        var shortcuts = Shortcuts.From(settings);

        Assert.Equal(
            [
                new Shortcut(Shortcuts.CycleOutputId, Parse("Win+Alt+A"), null),
                new Shortcut(Shortcuts.DeviceId(0), Parse("Win+Alt+1"), Speakers),
                new Shortcut(Shortcuts.DeviceId(1), Parse("Win+Alt+2"), Technics),
            ],
            shortcuts);
        Assert.Equal(shortcuts.Count, shortcuts.Select(s => s.Id).Distinct().Count());
    }

    [Fact]
    public void SkipsRowsWithoutADeviceOrAChordAndAnOffCycle()
    {
        var settings = new AppSettings(CycleOutputHotkey: "",
            DeviceHotkeys: [new DeviceHotkey(Hotkey: "Win+Alt+1"), Speakers with { Hotkey = "" }, Technics]).Normalize();

        var shortcut = Assert.Single(Shortcuts.From(settings));

        Assert.Equal(Shortcuts.DeviceId(2), shortcut.Id); // ids follow row positions, as Settings looks them up
        Assert.Equal(Technics, shortcut.Device);
    }

    [Fact]
    public void SwitchesToAnActiveDevice() =>
        Assert.Equal(DevicePress.Switch, Shortcuts.Press(Speakers, [SpeakersEndpoint], "h"));

    [Fact]
    public void AlreadyDefault() =>
        Assert.Equal(DevicePress.AlreadyDefault, Shortcuts.Press(Speakers, [SpeakersEndpoint], "s"));

    [Theory]
    [InlineData("s")]
    [InlineData(null)]
    public void NotActive(string? defaultId) =>
        Assert.Equal(DevicePress.NotActive, Shortcuts.Press(Technics, [SpeakersEndpoint], defaultId));

    private static Hotkey Parse(string text) => Hotkey.TryParse(text, out var hotkey) ? hotkey : throw new ArgumentException(text);
}
