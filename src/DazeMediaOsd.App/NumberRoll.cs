using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace DazeMediaOsd.App;

/// <summary>
/// A number that rolls to its new value: the old one slides out and fades, the new one slides in from the other
/// side (up when it grows) (D-037). Built for a held key: a change mid-roll sends the number on screen out from
/// where it is, so steps every 30–60 ms read as one motion. Opacity and translation run on the composition
/// thread. UI thread only.
/// </summary>
internal sealed class NumberRoll
{
    private static readonly TimeSpan InDuration = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan OutDuration = TimeSpan.FromMilliseconds(100);
    private const double ShiftDip = 8;

    private readonly TextBlock _current;
    private readonly TextBlock _previous;
    private readonly Storyboard _roll = new();
    private readonly DoubleAnimation _inShift;
    private readonly DoubleAnimation _outOpacity;
    private readonly DoubleAnimation _outShift;
    private string _text = "";
    private int _value;

    /// <param name="current">Shows the value; base opacity 1.</param>
    /// <param name="previous">Laid over it, only for the roll; base opacity 0.</param>
    public NumberRoll(TextBlock current, TextBlock previous)
    {
        (_current, _previous) = (current, previous);
        var currentShift = new TranslateTransform();
        var previousShift = new TranslateTransform();
        current.RenderTransform = currentShift;
        previous.RenderTransform = previousShift;
        Add(current, "Opacity", InDuration, to: 1);
        _inShift = Add(currentShift, "Y", InDuration, to: 0);
        _outOpacity = Add(previous, "Opacity", OutDuration, to: 0);
        _outShift = Add(previousShift, "Y", OutDuration, to: 0);
    }

    /// <summary>
    /// Shows <paramref name="text"/> for <paramref name="value"/>, rolling when <paramref name="animate"/>. The same
    /// text as shown doesn't restart a roll; any non-rolling render, repeat or not, stops one.
    /// </summary>
    public void Run(string text, int value, bool animate, Style style)
    {
        _current.Style = style;
        if (text == _text)
        {
            // A new device at the same value must not keep showing the last device's outgoing number.
            if (!animate)
                _roll.Stop();
            return;
        }
        var (oldText, oldValue) = (_text, _value);
        (_text, _value) = (text, value);

        // Where the outgoing number is now: still partway in if a roll is running.
        var progress = Progress();
        _roll.Stop(); // Stop drops the animated values: back to the base ones
        _current.Text = text;
        if (!animate || oldText.Length == 0 || text.Length == 0)
            return;

        var up = value >= oldValue ? 1 : -1;
        _previous.Style = style;
        _previous.Text = oldText;
        _outOpacity.From = progress;
        _outShift.From = (double)_inShift.From! * (1 - progress);
        _outShift.To = -up * ShiftDip;
        _inShift.From = up * ShiftDip;
        _roll.Begin();
    }

    // Eased progress of the incoming number, 1 when no roll runs.
    private double Progress()
    {
        if (_roll.GetCurrentState() != ClockState.Active)
            return 1;
        var t = Math.Clamp(_roll.GetCurrentTime() / InDuration, 0, 1);
        return 1 - Math.Pow(1 - t, 3); // CubicEase EaseOut
    }

    private DoubleAnimation Add(DependencyObject target, string property, TimeSpan duration, double to)
    {
        var animation = new DoubleAnimation
        {
            From = 0,
            To = to,
            Duration = duration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        _roll.Children.Add(animation);
        return animation;
    }
}
