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
}
