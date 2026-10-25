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
    /// Applies <paramref name="swap"/>: through a fade when <paramref name="changed"/> and <paramref name="fade"/>,
    /// else at once. While fading out, the newest swap replaces the waiting one, so the last content goes in.
    /// </summary>
    public void Run(bool changed, bool fade, Action swap)
    {
        if (_swap is not null)
        {
            _swap = swap;
            return;
        }
        if (!changed || !fade)
        {
            swap();
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
