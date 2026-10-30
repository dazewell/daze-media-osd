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

    [Theory]
    [InlineData(WheelArea.Taskbar, WheelArea.Taskbar, true)]
    [InlineData(WheelArea.Taskbar, WheelArea.Apps, true)]
    [InlineData(WheelArea.Tray, WheelArea.Taskbar, true)]
    [InlineData(WheelArea.Apps, WheelArea.Apps, true)]
    [InlineData(WheelArea.Tray, WheelArea.Tray, true)]
    [InlineData(WheelArea.Apps, WheelArea.Tray, false)]
    [InlineData(WheelArea.Tray, WheelArea.Apps, false)]
    [InlineData(WheelArea.Off, WheelArea.Taskbar, false)]
    [InlineData(WheelArea.Taskbar, WheelArea.Off, false)]
    [InlineData(WheelArea.Off, WheelArea.Off, false)]
    public void AreasOverlapWhenOneTurnCanBeInBoth(WheelArea a, WheelArea b, bool overlap) =>
        Assert.Equal(overlap, WheelRules.Overlaps(a, b));

    [Theory]
    [InlineData(WheelArea.Taskbar, WheelModifier.None, WheelArea.Taskbar, WheelModifier.None, true)]
    [InlineData(WheelArea.Taskbar, WheelModifier.None, WheelArea.Apps, WheelModifier.None, true)]
    [InlineData(WheelArea.Apps, WheelModifier.Ctrl, WheelArea.Apps, WheelModifier.Ctrl, true)]
    [InlineData(WheelArea.Taskbar, WheelModifier.None, WheelArea.Taskbar, WheelModifier.Ctrl, false)]
    [InlineData(WheelArea.Apps, WheelModifier.None, WheelArea.Tray, WheelModifier.None, false)]
    [InlineData(WheelArea.Taskbar, WheelModifier.None, WheelArea.Off, WheelModifier.None, false)]
    [InlineData(WheelArea.Off, WheelModifier.Ctrl, WheelArea.Off, WheelModifier.Ctrl, false)]
    public void TwoActionsConflictOnlyWithTheSameKeyInTouchingAreas(
        WheelArea areaA, WheelModifier keyA, WheelArea areaB, WheelModifier keyB, bool conflict) =>
        Assert.Equal(conflict, WheelRules.Conflict(areaA, keyA, areaB, keyB));

    [Fact]
    public void ConflictIsExactlyWhenSomeTurnWouldRunBothActions()
    {
        WheelArea[] areas = [WheelArea.Taskbar, WheelArea.Apps, WheelArea.Tray, WheelArea.Off];
        WheelModifier[] keys = [WheelModifier.None, WheelModifier.Ctrl, WheelModifier.Shift, WheelModifier.Alt];
        TaskbarZone[] zones = [TaskbarZone.Apps, TaskbarZone.Tray, TaskbarZone.VolumeIcon];
        HeldModifiers[] held = [HeldModifiers.None, HeldModifiers.Ctrl, HeldModifiers.Shift, HeldModifiers.Alt, HeldModifiers.Win];

        foreach (var areaA in areas)
        {
            foreach (var keyA in keys)
            {
                foreach (var areaB in areas)
                {
                    foreach (var keyB in keys)
                    {
                        var shared = zones.Any(zone => held.Any(h =>
                            WheelRules.Matches(areaA, keyA, zone, h) && WheelRules.Matches(areaB, keyB, zone, h)));

                        Assert.Equal(shared, WheelRules.Conflict(areaA, keyA, areaB, keyB));
                    }
                }
            }
        }
    }
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
}

public sealed class WheelBrightnessTests : IDisposable
{
    private readonly FakeWheel _wheel = new();
    private readonly FakeBrightness _brightness = new();
    private AppSettings _settings = AppSettings.Default with { WheelBrightnessArea = WheelArea.Taskbar };

    private WheelBrightness Create() => new(_wheel, _brightness, () => _settings);

    public void Dispose()
    {
        _wheel.Dispose();
        _brightness.Dispose();
    }

    [Fact]
    public void ANotchStepsByTheSetPercent()
    {
        _settings = _settings with { WheelBrightnessStep = 3 };
        using var wheel = Create();

        _wheel.Turn(TaskbarZone.Apps, 120, HeldModifiers.Ctrl);
        _wheel.Turn(TaskbarZone.Tray, -240, HeldModifiers.Ctrl);

        Assert.Equal([3, -6], _brightness.Steps);
    }

    [Fact]
    public void ItDoesNothingUntilTurnedOn()
    {
        _settings = AppSettings.Default;
        using var wheel = Create();

        _wheel.Turn(TaskbarZone.Apps, 120, HeldModifiers.Ctrl);
        _wheel.Turn(TaskbarZone.Apps, 120);

        Assert.Empty(_brightness.Steps);
    }

    [Fact]
    public void OutsideTheAreaOrWithoutTheKeyNothingHappens()
    {
        _settings = _settings with { WheelBrightnessArea = WheelArea.Apps, WheelBrightnessModifier = WheelModifier.Alt };
        using var wheel = Create();

        _wheel.Turn(TaskbarZone.Tray, 120, HeldModifiers.Alt);
        _wheel.Turn(TaskbarZone.Apps, 120);
        _wheel.Turn(TaskbarZone.Apps, 120, HeldModifiers.Ctrl);
        _wheel.Turn(TaskbarZone.VolumeIcon, 120, HeldModifiers.Alt);

        Assert.Empty(_brightness.Steps);
    }

    [Fact]
    public void SmallDeltasAddUpToANotch()
    {
        using var wheel = Create();

        for (var i = 0; i < 16; i++)
            _wheel.Turn(TaskbarZone.Apps, 15, HeldModifiers.Ctrl);

        Assert.Equal([5, 5], _brightness.Steps);
    }

    [Fact]
    public void VolumeKeepsATurnBothActionsWouldTake()
    {
        // Settings never allow this, but a hand-edited file or a race must not make one turn do both.
        _settings = _settings with { WheelBrightnessModifier = WheelModifier.None };
        using var wheel = Create();

        _wheel.Turn(TaskbarZone.Apps, 120);

        Assert.Empty(_brightness.Steps);
    }

    [Fact]
    public void VolumeAndBrightnessShareTheWheelOnDifferentKeys()
    {
        using var audio = new FakeAudio();
        using var volume = new WheelVolume(_wheel, audio, () => _settings);
        using var brightness = Create();

        _wheel.Turn(TaskbarZone.Apps, 120);
        _wheel.Turn(TaskbarZone.Apps, 120, HeldModifiers.Ctrl);

        Assert.Equal([0.02f], audio.VolumeSteps);
        Assert.Equal([5], _brightness.Steps);
    }

    [Fact]
    public void ReadsTheSettingsAtEachNotch()
    {
        using var wheel = Create();
        _wheel.Turn(TaskbarZone.Apps, 120, HeldModifiers.Ctrl);

        _settings = _settings with { WheelBrightnessArea = WheelArea.Off };
        _wheel.Turn(TaskbarZone.Apps, 120, HeldModifiers.Ctrl);

        Assert.Single(_brightness.Steps);
    }

    [Fact]
    public void DisposeStopsListening()
    {
        Create().Dispose();

        _wheel.Turn(TaskbarZone.Apps, 120, HeldModifiers.Ctrl);

        Assert.Empty(_brightness.Steps);
    }
}

public class WheelSettingsTests
{
    [Fact]
    public void BrightnessIsOffUntilTurnedOnAndTheVolumeKeepsItsDefaults()
    {
        Assert.Equal((WheelArea.Off, WheelModifier.Ctrl, 5),
            (AppSettings.Default.WheelBrightnessArea, AppSettings.Default.WheelBrightnessModifier, AppSettings.Default.WheelBrightnessStep));
        Assert.Equal((WheelArea.Taskbar, WheelModifier.None, 2),
            (AppSettings.Default.WheelVolumeArea, AppSettings.Default.WheelVolumeModifier, AppSettings.Default.WheelVolumeStep));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(50, 10)]
    [InlineData(4, 4)]
    public void BrightnessStepIsClamped(int step, int expected) =>
        Assert.Equal(expected, (AppSettings.Default with { WheelBrightnessStep = step }).Normalize().WheelBrightnessStep);

    [Fact]
    public void UnknownBrightnessEnumsFallBackToDefaults()
    {
        var settings = (AppSettings.Default with { WheelBrightnessArea = (WheelArea)42, WheelBrightnessModifier = (WheelModifier)42 }).Normalize();

        Assert.Equal(AppSettings.Default.WheelBrightnessArea, settings.WheelBrightnessArea);
        Assert.Equal(AppSettings.Default.WheelBrightnessModifier, settings.WheelBrightnessModifier);
    }

    [Fact]
    public void BrightnessSettingsTakePartInEquality()
    {
        Assert.NotEqual(AppSettings.Default, AppSettings.Default with { WheelBrightnessStep = 6 });
        Assert.NotEqual(AppSettings.Default, AppSettings.Default with { WheelBrightnessArea = WheelArea.Tray });
        Assert.NotEqual(AppSettings.Default, AppSettings.Default with { WheelBrightnessModifier = WheelModifier.Alt });
        Assert.Contains("WheelBrightnessArea = Off", AppSettings.Default.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(WheelArea.Taskbar, WheelModifier.None)] // the same area and key as the volume's default
    [InlineData(WheelArea.Apps, WheelModifier.None)] // inside the volume's area
    [InlineData(WheelArea.Tray, WheelModifier.None)]
    public void BrightnessYieldsToTheVolumeWhenTheyWouldShareATurn(WheelArea area, WheelModifier key)
    {
        var settings = (AppSettings.Default with { WheelBrightnessArea = area, WheelBrightnessModifier = key }).Normalize();

        Assert.Equal(WheelArea.Off, settings.WheelBrightnessArea);
        Assert.Equal(AppSettings.Default.WheelVolumeArea, settings.WheelVolumeArea);
    }

    [Theory]
    [InlineData(WheelArea.Apps, WheelModifier.None, WheelArea.Tray, WheelModifier.None)] // the areas don't touch
    [InlineData(WheelArea.Taskbar, WheelModifier.None, WheelArea.Taskbar, WheelModifier.Ctrl)] // another key
    [InlineData(WheelArea.Off, WheelModifier.None, WheelArea.Taskbar, WheelModifier.None)] // the volume is off
    public void BrightnessIsLeftAloneWhenItsTurnsAreItsOwn(WheelArea volume, WheelModifier volumeKey, WheelArea brightness, WheelModifier brightnessKey)
    {
        var settings = (AppSettings.Default with
        {
            WheelVolumeArea = volume, WheelVolumeModifier = volumeKey,
            WheelBrightnessArea = brightness, WheelBrightnessModifier = brightnessKey,
        }).Normalize();

        Assert.Equal((brightness, brightnessKey), (settings.WheelBrightnessArea, settings.WheelBrightnessModifier));
    }

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
