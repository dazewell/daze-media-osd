using DazeMediaOsd.Core.Osd;

namespace DazeMediaOsd.Core.Tests;

public class OsdAnimationTests
{
    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(200);

    private static TimeSpan Ms(double ms) => TimeSpan.FromMilliseconds(ms);

    [Fact]
    public void StartsHidden()
    {
        var animation = new OsdAnimation(Duration, Duration);
        Assert.True(animation.IsHidden);
        Assert.False(animation.IsRunning);
        Assert.Equal(0, animation.Visibility);
    }

    [Fact]
    public void ShowEasesInToFullyVisible()
    {
        var animation = new OsdAnimation(Duration, Duration);
        animation.Show(Ms(0), animate: true);
        Assert.False(animation.IsHidden);
        Assert.True(animation.IsRunning);

        Assert.True(animation.Tick(Ms(100)));
        Assert.Equal(0.875, animation.Visibility, 6); // ease-out: most of the way at half time

        Assert.False(animation.Tick(Ms(200)));
        Assert.Equal(1, animation.Visibility);
        Assert.False(animation.IsRunning);
    }

    [Fact]
    public void ShowWhileShowingOrShownChangesNothing()
    {
        var animation = new OsdAnimation(Duration, Duration);
        animation.Show(Ms(0), animate: true);
        animation.Tick(Ms(100));
        animation.Show(Ms(100), animate: true); // held key
        Assert.False(animation.Tick(Ms(200)));
        Assert.Equal(1, animation.Visibility);

        animation.Show(Ms(300), animate: true);
        Assert.False(animation.IsRunning);
        Assert.Equal(1, animation.Visibility);
    }

    [Fact]
    public void HideFadesOutThenIsHidden()
    {
        var animation = new OsdAnimation(Duration, Duration);
        animation.Show(Ms(0), animate: false);
        animation.Hide(Ms(1000), animate: true);
        Assert.False(animation.IsHidden);

        animation.Tick(Ms(1100));
        Assert.Equal(0.875, animation.Visibility, 6); // ease-in: barely faded at half time

        Assert.False(animation.Tick(Ms(1200)));
        Assert.True(animation.IsHidden);
    }

    [Fact]
    public void ShowDuringHideReversesFromTheCurrentValue()
    {
        var animation = new OsdAnimation(Duration, Duration);
        animation.Show(Ms(0), animate: false);
        animation.Hide(Ms(0), animate: true);
        animation.Tick(Ms(150));
        var faded = animation.Visibility;

        animation.Show(Ms(150), animate: true);
        Assert.Equal(faded, animation.Visibility, 6); // no jump
        Assert.True(animation.Tick(Ms(160)));
        Assert.True(animation.Visibility > faded);
        Assert.False(animation.Tick(Ms(400)));
        Assert.Equal(1, animation.Visibility);
    }

    [Fact]
    public void WithoutAnimationJumpsAtOnce()
    {
        var animation = new OsdAnimation(Duration, Duration);
        animation.Show(Ms(0), animate: false);
        Assert.Equal(1, animation.Visibility);
        Assert.False(animation.IsRunning);

        animation.Hide(Ms(10), animate: false);
        Assert.True(animation.IsHidden);
        Assert.False(animation.IsRunning);
    }
}
