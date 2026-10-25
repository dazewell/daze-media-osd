using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Animation;

namespace DazeMediaOsd.App;

/// <summary>
/// Fluent "fade through" for content that changes while the OSD is up: fade out, swap, fade in (D-034).
/// Opacity animations run on the composition thread. UI thread only.
/// </summary>
internal sealed class FadeThrough
{
    private static readonly TimeSpan OutDuration = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan InDuration = TimeSpan.FromMilliseconds(150);

    private readonly Storyboard _out;
    private readonly Storyboard _in;
    private Action? _swap;
    private string? _content; // the last asked for: on screen, or waiting in _swap

    public FadeThrough(UIElement target)
    {
        // No From: each starts where the other left off, so a change during the fade-in turns smoothly.
        _out = Make(target, 0, OutDuration, EasingMode.EaseIn);
        _in = Make(target, 1, InDuration, EasingMode.EaseOut);
        _out.Completed += (_, _) =>
        {
            var swap = _swap;
            _swap = null;
            swap?.Invoke();
            _in.Begin();
        };
    }

    /// <summary>
    /// Shows <paramref name="content"/> by running <paramref name="swap"/>, through a fade when
    /// <paramref name="fade"/>. The same content as last asked for (shown or waiting) does nothing, so a repeat
    /// render doesn't disturb a fade. A non-fading change cancels a running fade (a level step or a fresh show
    /// must not wait); a fading one during the fade-out replaces the waiting swap.
    /// </summary>
    public void Run(string content, bool fade, Action swap)
    {
        if (content == _content)
            return;
        _content = content;
        if (!fade)
        {
            _swap = null;
            _out.Stop(); // Stop drops the animated opacity: back to the base 1
            _in.Stop();
            swap();
            return;
        }
        if (_swap is not null)
        {
            _swap = swap;
            return;
        }
        _swap = swap;
        _out.Begin();
    }

    private static Storyboard Make(UIElement target, double to, TimeSpan duration, EasingMode mode)
    {
        var animation = new DoubleAnimation
        {
            To = to,
            Duration = duration,
            EasingFunction = new CubicEase { EasingMode = mode },
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, "Opacity");
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        return storyboard;
    }
}

/// <summary>One <see cref="FadeThrough"/> per element, made on first use.</summary>
internal sealed class FadeThroughs
{
    private readonly Dictionary<UIElement, FadeThrough> _byElement = [];

    public FadeThrough Get(UIElement element)
    {
        if (!_byElement.TryGetValue(element, out var fade))
            _byElement[element] = fade = new FadeThrough(element);
        return fade;
    }
}
