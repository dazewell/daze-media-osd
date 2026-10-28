using DazeMediaOsd.Core.Audio;
using DazeMediaOsd.Core.Diagnostics;
using DazeMediaOsd.Core.Settings;

namespace DazeMediaOsd.Core.Input;

/// <summary>The wheel over the taskbar changes the output volume, where and with the key the settings say (D-036).</summary>
public sealed class WheelVolume : IDisposable
{
    private readonly ITaskbarWheelSource _source;
    private readonly IAudioService _audio;
    private readonly Func<AppSettings> _settings;
    private readonly WheelAccumulator _notches = new(); // only touched on the source thread

    public WheelVolume(ITaskbarWheelSource source, IAudioService audio, Func<AppSettings> settings)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _audio = audio ?? throw new ArgumentNullException(nameof(audio));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _source.Wheeled += OnWheeled;
    }

    /// <summary>
    /// A step was asked for. Raised on the source thread; show the OSD, like a volume key: at 0 % or 100 % no
    /// volume change follows to show it.
    /// </summary>
    public event EventHandler? Stepped;

    private void OnWheeled(object? sender, TaskbarWheelEventArgs e)
    {
        var settings = _settings();
        if (!WheelRules.Matches(settings.WheelVolumeArea, settings.WheelVolumeModifier, e.Zone, e.Held))
            return;
        var notches = _notches.Add(e.Delta);
        if (notches == 0)
            return;
        _audio.StepOutputVolume(notches * settings.WheelVolumeStep / 100f);
        try
        {
            Stepped?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Error("wheel volume handler", ex);
        }
    }

    public void Dispose() => _source.Wheeled -= OnWheeled;
}
