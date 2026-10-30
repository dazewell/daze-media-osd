using DazeMediaOsd.Core.Brightness;
using DazeMediaOsd.Core.Settings;

namespace DazeMediaOsd.Core.Input;

/// <summary>The wheel over the taskbar changes the built-in screen's brightness, where and with the key the settings say (D-040).</summary>
public sealed class WheelBrightness : IDisposable
{
    private readonly ITaskbarWheelSource _source;
    private readonly IBrightnessService _brightness;
    private readonly Func<AppSettings> _settings;
    private readonly WheelAccumulator _notches = new(); // only touched on the source thread

    public WheelBrightness(ITaskbarWheelSource source, IBrightnessService brightness, Func<AppSettings> settings)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _brightness = brightness ?? throw new ArgumentNullException(nameof(brightness));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _source.Wheeled += OnWheeled;
    }

    private void OnWheeled(object? sender, TaskbarWheelEventArgs e)
    {
        var settings = _settings();
        if (!WheelRules.Matches(settings.WheelBrightnessArea, settings.WheelBrightnessModifier, e.Zone, e.Held))
            return;
        // The settings never allow two actions on one turn; if one slips through anyway, the volume keeps it.
        if (WheelRules.Matches(settings.WheelVolumeArea, settings.WheelVolumeModifier, e.Zone, e.Held))
            return;
        var notches = _notches.Add(e.Delta);
        if (notches != 0)
            _brightness.Adjust(notches * settings.WheelBrightnessStep);
    }

    public void Dispose() => _source.Wheeled -= OnWheeled;
}
