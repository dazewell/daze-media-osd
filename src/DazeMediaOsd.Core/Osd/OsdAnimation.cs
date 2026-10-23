namespace DazeMediaOsd.Core.Osd;

/// <summary>
/// Show/hide progress of the OSD: <see cref="Visibility"/> goes 0 → 1 (ease-out) on show and 1 → 0 (ease-in)
/// on hide. Reversing mid-way continues from the current value, and a show while showing or shown changes
/// nothing, so a held key or a refresh never replays the animation. Time is supplied by the caller.
/// </summary>
public sealed class OsdAnimation(TimeSpan showDuration, TimeSpan hideDuration)
{
    private bool _showing;
    private TimeSpan _start;
    private double _startProgress = 1; // linear progress of the current direction at _start

    /// <summary>0 = hidden, 1 = fully shown; eased.</summary>
    public double Visibility { get; private set; }

    /// <summary>True while a show or hide is under way, i.e. <see cref="Tick"/> still changes something.</summary>
    public bool IsRunning => _startProgress < 1;

    /// <summary>Hidden and not about to show: the window can be hidden.</summary>
    public bool IsHidden => !_showing && Visibility == 0;

    public void Show(TimeSpan now, bool animate)
    {
        if (_showing)
            return;
        _showing = true;
        Start(now, animate, 1 - Math.Cbrt(1 - Visibility)); // inverse of the ease-out
    }

    public void Hide(TimeSpan now, bool animate)
    {
        if (!_showing)
            return;
        _showing = false;
        Start(now, animate, Math.Cbrt(1 - Visibility)); // inverse of the ease-in
    }

    /// <summary>Advances to <paramref name="now"/>; false once the current direction has finished.</summary>
    public bool Tick(TimeSpan now)
    {
        var t = Progress(now);
        Visibility = _showing ? 1 - Math.Pow(1 - t, 3) : 1 - Math.Pow(t, 3);
        if (t >= 1)
            _startProgress = 1;
        return t < 1;
    }

    private void Start(TimeSpan now, bool animate, double progress)
    {
        _start = now;
        _startProgress = animate ? progress : 1;
        Tick(now);
    }

    private double Progress(TimeSpan now)
    {
        if (_startProgress >= 1)
            return 1;
        var duration = _showing ? showDuration : hideDuration;
        return duration <= TimeSpan.Zero ? 1 : Math.Min(1, _startProgress + (now - _start) / duration);
    }
}
