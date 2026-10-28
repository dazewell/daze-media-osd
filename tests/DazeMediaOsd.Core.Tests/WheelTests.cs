using DazeMediaOsd.Core.Input;
using DazeMediaOsd.Core.Settings;

namespace DazeMediaOsd.Core.Tests;

public class WheelRulesTests
{
    [Theory]
    [InlineData(WheelArea.Taskbar, TaskbarZone.Apps, true)]
    [InlineData(WheelArea.Taskbar, TaskbarZone.Tray, true)]
    [InlineData(WheelArea.Apps, TaskbarZone.Apps, true)]
    [InlineData(WheelArea.Apps, TaskbarZone.Tray, false)]
    [InlineData(WheelArea.Tray, TaskbarZone.Tray, true)]
    [InlineData(WheelArea.Tray, TaskbarZone.Apps, false)]
    [InlineData(WheelArea.Off, TaskbarZone.Apps, false)]
    [InlineData(WheelArea.Off, TaskbarZone.Tray, false)]
    public void AreaPicksTheZones(WheelArea area, TaskbarZone zone, bool matches) =>
        Assert.Equal(matches, WheelRules.Matches(area, WheelModifier.None, zone, HeldModifiers.None));

    [Theory]
    [InlineData(WheelArea.Taskbar)]
    [InlineData(WheelArea.Tray)]
    public void TheSpeakerIconIsLeftToWindows(WheelArea area) =>
        Assert.False(WheelRules.Matches(area, WheelModifier.None, TaskbarZone.VolumeIcon, HeldModifiers.None));

    [Theory]
    [InlineData(WheelModifier.None, HeldModifiers.None, true)]
    [InlineData(WheelModifier.None, HeldModifiers.Ctrl, false)] // Ctrl+wheel stays free for another action
    [InlineData(WheelModifier.Ctrl, HeldModifiers.Ctrl, true)]
    [InlineData(WheelModifier.Ctrl, HeldModifiers.None, false)]
    [InlineData(WheelModifier.Ctrl, HeldModifiers.Ctrl | HeldModifiers.Shift, false)]
    [InlineData(WheelModifier.Shift, HeldModifiers.Shift, true)]
    [InlineData(WheelModifier.Alt, HeldModifiers.Alt, true)]
    [InlineData(WheelModifier.None, HeldModifiers.Win, false)]
    public void TheKeyHeldMustBeExactlyTheOneSet(WheelModifier modifier, HeldModifiers held, bool matches) =>
        Assert.Equal(matches, WheelRules.Matches(WheelArea.Taskbar, modifier, TaskbarZone.Apps, held));
}

public class WheelAccumulatorTests
{
    [Fact]
    public void AClassicNotchIsOneStep()
    {
        var notches = new WheelAccumulator();

        Assert.Equal(1, notches.Add(120));
        Assert.Equal(-1, notches.Add(-120));
        Assert.Equal(2, notches.Add(240));
    }

    [Fact]
    public void SmallDeltasAddUpToANotch()
    {
        var notches = new WheelAccumulator();

        var steps = Enumerable.Range(0, 16).Select(_ => notches.Add(15)).ToList();

        Assert.Equal([0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 1], steps);
    }

    [Fact]
    public void TurningBackDropsTheRest()
    {
        var notches = new WheelAccumulator();
        notches.Add(100);

        Assert.Equal(0, notches.Add(-60)); // not -1 after a net +40
        Assert.Equal(-1, notches.Add(-60));
    }
}

public sealed class WheelVolumeTests : IDisposable
{
    private readonly FakeWheel _wheel = new();
    private readonly FakeAudio _audio = new();
    private AppSettings _settings = AppSettings.Default;

    private WheelVolume Create() => new(_wheel, _audio, () => _settings);

    public void Dispose()
    {
        _wheel.Dispose();
        _audio.Dispose();
    }

    [Fact]
    public void ANotchStepsByTheSetPercent()
    {
        _settings = _settings with { WheelVolumeStep = 5 };
        using var volume = Create();

        _wheel.Turn(TaskbarZone.Apps, 120);
        _wheel.Turn(TaskbarZone.Tray, -240);

        Assert.Equal([0.05f, -0.10f], _audio.VolumeSteps);
    }

    [Fact]
    public void SteppedRaisesOncePerStep()
    {
        using var volume = Create();
        var raised = 0;
        volume.Stepped += (_, _) => raised++;

        _wheel.Turn(TaskbarZone.Apps, 60);
        _wheel.Turn(TaskbarZone.Apps, 60);

        Assert.Equal(1, raised);
        Assert.Single(_audio.VolumeSteps);
    }

    [Fact]
    public void OutsideTheAreaOrWithoutTheKeyNothingHappens()
    {
        _settings = _settings with { WheelVolumeArea = WheelArea.Apps, WheelVolumeModifier = WheelModifier.Ctrl };
        using var volume = Create();

        _wheel.Turn(TaskbarZone.Tray, 120, HeldModifiers.Ctrl);
        _wheel.Turn(TaskbarZone.Apps, 120);
        _wheel.Turn(TaskbarZone.VolumeIcon, 120, HeldModifiers.Ctrl);

        Assert.Empty(_audio.VolumeSteps);
    }

    [Fact]
    public void ReadsTheSettingsAtEachNotch()
    {
        using var volume = Create();
        _wheel.Turn(TaskbarZone.Apps, 120);

        _settings = _settings with { WheelVolumeArea = WheelArea.Off };
        _wheel.Turn(TaskbarZone.Apps, 120);

        Assert.Single(_audio.VolumeSteps);
    }

    [Fact]
    public void DisposeStopsListening()
    {
        Create().Dispose();

        _wheel.Turn(TaskbarZone.Apps, 120);

        Assert.Empty(_audio.VolumeSteps);
    }

    private sealed class FakeWheel : ITaskbarWheelSource
    {
        public event EventHandler<TaskbarWheelEventArgs>? Wheeled;

        public void Turn(TaskbarZone zone, int delta, HeldModifiers held = HeldModifiers.None) =>
            Wheeled?.Invoke(this, new TaskbarWheelEventArgs(zone, delta, held));

        public void Dispose()
        {
        }
    }
}

public class WheelSettingsTests
{
    [Fact]
    public void DefaultsChangeTheVolumeAnywhereWithThePlainWheel()
    {
        Assert.Equal(WheelArea.Taskbar, AppSettings.Default.WheelVolumeArea);
        Assert.Equal(WheelModifier.None, AppSettings.Default.WheelVolumeModifier);
        Assert.Equal(2, AppSettings.Default.WheelVolumeStep);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(50, 10)]
    [InlineData(4, 4)]
    public void StepIsClamped(int step, int expected) =>
        Assert.Equal(expected, (AppSettings.Default with { WheelVolumeStep = step }).Normalize().WheelVolumeStep);

    [Fact]
    public void UnknownEnumsFallBackToDefaults()
    {
        var settings = (AppSettings.Default with { WheelVolumeArea = (WheelArea)42, WheelVolumeModifier = (WheelModifier)42 }).Normalize();

        Assert.Equal(AppSettings.Default.WheelVolumeArea, settings.WheelVolumeArea);
        Assert.Equal(AppSettings.Default.WheelVolumeModifier, settings.WheelVolumeModifier);
    }

    [Fact]
    public void WheelSettingsTakePartInEquality() =>
        Assert.NotEqual(AppSettings.Default, AppSettings.Default with { WheelVolumeStep = 3 });
}
