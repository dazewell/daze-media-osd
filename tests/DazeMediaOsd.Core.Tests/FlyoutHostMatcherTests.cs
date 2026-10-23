using DazeMediaOsd.Core.NativeFlyout;

namespace DazeMediaOsd.Core.Tests;

public class FlyoutHostMatcherTests
{
    [Theory]
    [InlineData("XamlExplorerHostIslandWindow", "explorer", 18u, true)]
    [InlineData("XamlExplorerHostIslandWindow", "Explorer", 18u, true)]
    [InlineData("XamlExplorerHostIslandWindow", "explorer", 16u, false)] // "Task Switching"
    [InlineData("XamlExplorerHostIslandWindow", "ShellHost", 18u, false)]
    [InlineData("NativeHWNDHost", "explorer", 18u, false)]                 // Windows 10 host
    public void MatchesOnlyExplorersAboveLockHost(string className, string process, uint band, bool expected) =>
        Assert.Equal(expected, FlyoutHostMatcher.IsFlyoutHost(className, process, band));
}
