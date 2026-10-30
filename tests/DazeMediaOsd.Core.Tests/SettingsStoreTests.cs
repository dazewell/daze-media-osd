using DazeMediaOsd.Core.Audio;
using DazeMediaOsd.Core.Settings;

namespace DazeMediaOsd.Core.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "DazeMediaOsd.Tests", Guid.NewGuid().ToString("N"));

    private string SettingsFile => Path.Combine(_directory, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void MissingFileGivesDefaults() =>
        Assert.Equal(AppSettings.Default, new SettingsStore(_directory).Current);

    [Fact]
    public void UpdateRoundTrips()
    {
        new SettingsStore(_directory).Update(s => s with { Position = OsdPosition.TopRight, HideAfterSeconds = 4, ShowInput = false });

        var reloaded = new SettingsStore(_directory).Current;

        Assert.Equal(new AppSettings { Position = OsdPosition.TopRight, HideAfterSeconds = 4, ShowInput = false }, reloaded);
    }

    [Fact]
    public void WheelSettingsRoundTrip()
    {
        new SettingsStore(_directory).Update(s => s with
        {
            WheelVolumeArea = WheelArea.Tray, WheelVolumeModifier = WheelModifier.Alt, WheelVolumeStep = 5,
        });

        var reloaded = new SettingsStore(_directory).Current;

        Assert.Equal((WheelArea.Tray, WheelModifier.Alt, 5), (reloaded.WheelVolumeArea, reloaded.WheelVolumeModifier, reloaded.WheelVolumeStep));
    }

    [Fact]
    public void AFileWithoutWheelSettingsGetsTheDefaults()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsFile, """{ "position": "topLeft" }""");

        var settings = new SettingsStore(_directory).Current;

        Assert.Equal((WheelArea.Taskbar, 2), (settings.WheelVolumeArea, settings.WheelVolumeStep));
    }

    [Fact]
    public void BrightnessSettingsRoundTrip()
    {
        new SettingsStore(_directory).Update(s => s with
        {
            WheelBrightnessArea = WheelArea.Tray, WheelBrightnessModifier = WheelModifier.Alt, WheelBrightnessStep = 3,
        });

        var reloaded = new SettingsStore(_directory).Current;

        Assert.Equal(
            (WheelArea.Tray, WheelModifier.Alt, 3),
            (reloaded.WheelBrightnessArea, reloaded.WheelBrightnessModifier, reloaded.WheelBrightnessStep));
    }

    [Fact]
    public void AFileFromBeforeTheBrightnessWheelLeavesItOff()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsFile, """{ "wheelVolumeArea": "tray", "wheelVolumeStep": 4 }""");

        var settings = new SettingsStore(_directory).Current;

        Assert.Equal(
            (WheelArea.Off, WheelModifier.Ctrl, 5),
            (settings.WheelBrightnessArea, settings.WheelBrightnessModifier, settings.WheelBrightnessStep));
        Assert.Equal((WheelArea.Tray, 4), (settings.WheelVolumeArea, settings.WheelVolumeStep));
    }

    [Fact]
    public void AHandEditedFileCannotMakeBrightnessAndVolumeShareATurn()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsFile, """{ "wheelBrightnessArea": "taskbar", "wheelBrightnessModifier": "none" }""");

        var settings = new SettingsStore(_directory).Current;

        Assert.Equal(WheelArea.Off, settings.WheelBrightnessArea);
        Assert.Equal(WheelArea.Taskbar, settings.WheelVolumeArea);
    }

    [Fact]
    public void EnumsAreWrittenAsNames()
    {
        new SettingsStore(_directory).Update(s => s with { Position = OsdPosition.TopLeft });

        Assert.Contains("\"topLeft\"", File.ReadAllText(SettingsFile), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ChangedFiresOnlyOnRealChange()
    {
        var store = new SettingsStore(_directory);
        var raised = 0;
        store.Changed += (_, _) => raised++;

        store.Update(s => s);
        store.Update(s => s with { ShowInput = false });

        Assert.Equal(1, raised);
    }

    [Fact]
    public void OutOfRangeValuesAreClamped()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsFile, """{ "hideAfterSeconds": 99, "position": 42 }""");

        var settings = new SettingsStore(_directory).Current;

        Assert.Equal(AppSettings.MaxHideAfterSeconds, settings.HideAfterSeconds);
        Assert.Equal(OsdPosition.BottomCenter, settings.Position);
    }

    [Fact]
    public void MissingPropertiesKeepDefaults()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsFile, """{ "showInput": false, "futureThing": 1 }""");

        Assert.Equal(AppSettings.Default with { ShowInput = false }, new SettingsStore(_directory).Current);
    }

    [Fact]
    public void FileFromBeforeShowMediaGetsItOn()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsFile, """{ "version": 1, "position": "BottomRight", "hideAfterSeconds": 2.5, "showInput": false }""");

        Assert.True(new SettingsStore(_directory).Current.ShowMedia);
    }

    [Fact]
    public void FileFromBeforeShowOnDeviceConnectGetsItOn()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsFile, """{ "version": 1, "showInput": true, "showMedia": false }""");

        Assert.True(new SettingsStore(_directory).Current.ShowOnDeviceConnect);
    }

    [Fact]
    public void FileFromBeforeShowOnMediaChangeGetsItOff()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsFile, """{ "version": 1, "showMedia": true }""");

        Assert.False(new SettingsStore(_directory).Current.ShowOnMediaChange);
    }

    [Theory]
    [InlineData("""{ "version": 1 }""", "Win+Alt+A")]                          // file from before the hotkey
    [InlineData("""{ "cycleOutputHotkey": "" }""", "")]                       // off
    [InlineData("""{ "cycleOutputHotkey": "alt+ctrl+f11" }""", "Ctrl+Alt+F11")] // canonical form
    [InlineData("""{ "cycleOutputHotkey": "Alt+Banana" }""", "Win+Alt+A")]      // typo: default, not off
    [InlineData("""{ "cycleOutputHotkey": null }""", "Win+Alt+A")]
    public void CycleOutputHotkeyIsNormalized(string json, string expected)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsFile, json);

        Assert.Equal(expected, new SettingsStore(_directory).Current.CycleOutputHotkey);
    }

    [Fact]
    public void HotkeyIsWrittenReadably()
    {
        new SettingsStore(_directory).Update(s => s with { CycleOutputHotkey = "Ctrl+Alt+F11" });

        Assert.Contains("\"Ctrl+Alt+F11\"", File.ReadAllText(SettingsFile), StringComparison.Ordinal);
    }

    [Fact]
    public void FailedSaveKeepsTheChangeAndReportsIt()
    {
        var store = new SettingsStore(_directory);
        Directory.CreateDirectory(SettingsFile); // a folder where the file goes: the move fails

        store.Update(s => s with { ShowInput = false });

        Assert.False(store.Current.ShowInput);
        Assert.NotNull(store.SaveError);
        Assert.False(File.Exists(SettingsFile + ".tmp"));

        Directory.Delete(SettingsFile);
        store.Update(s => s with { ShowInput = true });

        Assert.Null(store.SaveError);
        Assert.True(new SettingsStore(_directory).Current.ShowInput);
    }

    [Fact]
    public void InvalidFileGivesDefaultsAndIsKeptAside()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsFile, "{ not json");

        Assert.Equal(AppSettings.Default, new SettingsStore(_directory).Current);
        Assert.Equal("{ not json", File.ReadAllText(Path.Combine(_directory, "settings.invalid.json")));
    }
    [Fact]
    public void DeviceHotkeysRoundTrip()
    {
        DeviceHotkey[] rows = [new("{out}", EndpointFlow.Output, "Speakers", "Win+Alt+1"), new("{in}", EndpointFlow.Input, "Mic", "")];
        new SettingsStore(_directory).Update(s => s with { DeviceHotkeys = rows });

        Assert.Equal(rows, new SettingsStore(_directory).Current.DeviceHotkeys);
        Assert.Contains("\"Win+Alt+1\"", File.ReadAllText(SettingsFile), StringComparison.Ordinal);
    }

    [Fact]
    public void FileFromBeforeDeviceHotkeysHasNone()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsFile, """{ "version": 1, "cycleOutputHotkey": "Win+Alt+A" }""");

        Assert.Empty(new SettingsStore(_directory).Current.DeviceHotkeys);
    }

    [Fact]
    public void DeviceHotkeysAreNormalized()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsFile, """
            { "cycleOutputHotkey": "Win+Alt+A", "deviceHotkeys": [
                { "id": "a", "flow": "Output", "name": "A", "hotkey": "alt+win+1" },
                { "id": "b", "flow": "Input", "name": "B", "hotkey": "Alt+Banana" },
                { "id": "c", "flow": "Output", "name": "C", "hotkey": "Win+Alt+A" },
                { "id": "d", "flow": "Output", "name": "D", "hotkey": "Win+Alt+1" },
                { "id": null, "name": null, "hotkey": null },
                null
            ] }
            """);

        Assert.Equal(
            [
                new DeviceHotkey("a", EndpointFlow.Output, "A", "Win+Alt+1"), // canonical form
                new DeviceHotkey("b", EndpointFlow.Input, "B", ""),           // not a chord
                new DeviceHotkey("c", EndpointFlow.Output, "C", ""),          // the cycle shortcut's
                new DeviceHotkey("d", EndpointFlow.Output, "D", ""),          // an earlier row's
                new DeviceHotkey(),
            ],
            new SettingsStore(_directory).Current.DeviceHotkeys);
    }

    [Fact]
    public void UnknownFlowDropsTheRow()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsFile, """{ "deviceHotkeys": [ { "id": "a", "flow": 7, "hotkey": "Win+Alt+1" } ] }""");

        Assert.Empty(new SettingsStore(_directory).Current.DeviceHotkeys);
    }

    [Fact]
    public void EqualDeviceHotkeysAreNoChange()
    {
        var store = new SettingsStore(_directory);
        store.Update(s => s with { DeviceHotkeys = [new DeviceHotkey("a", EndpointFlow.Output, "A", "Win+Alt+1")] });
        var raised = 0;
        store.Changed += (_, _) => raised++;

        store.Update(s => s with { DeviceHotkeys = [new DeviceHotkey("a", EndpointFlow.Output, "A", "Win+Alt+1")] });

        Assert.Equal(0, raised);
    }

    [Fact]
    public void ChangingTheCycleShortcutToARowsChordDropsItFromTheRow()
    {
        var settings = new AppSettings(
            CycleOutputHotkey: "Win+Alt+1", DeviceHotkeys: [new DeviceHotkey("a", EndpointFlow.Output, "A", "Win+Alt+1")]).Normalize();

        Assert.Equal("", settings.DeviceHotkeys[0].Hotkey);
    }
}
