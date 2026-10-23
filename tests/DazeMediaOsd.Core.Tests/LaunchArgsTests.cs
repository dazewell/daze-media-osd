using DazeMediaOsd.Core.Lifecycle;

namespace DazeMediaOsd.Core.Tests;

public class LaunchArgsTests
{
    [Fact]
    public void NoArgumentsIsLauncher() =>
        Assert.Equal(new LaunchArgs(LaunchMode.Launcher, null), LaunchArgs.Parse([]));

    [Fact]
    public void ForAppRoundTrips()
    {
        var parsed = LaunchArgs.Parse(LaunchArgs.ForApp(4242).Split(' '));
        Assert.Equal(new LaunchArgs(LaunchMode.App, 4242), parsed);
    }

    [Fact]
    public void AppWithoutLauncherPidHasNoGuard() =>
        Assert.Equal(new LaunchArgs(LaunchMode.App, null), LaunchArgs.Parse(["--app"]));

    [Fact]
    public void RestoreSwitchSelectsRestore() =>
        Assert.Equal(LaunchMode.Restore, LaunchArgs.Parse(["--restore"]).Mode);

    [Theory]
    [InlineData("--launcher-pid")]
    [InlineData("--launcher-pid", "abc")]
    [InlineData("--launcher-pid", "-5")]
    public void MalformedLauncherPidIsIgnored(params string[] tail) =>
        Assert.Null(LaunchArgs.Parse(["--app", .. tail]).LauncherPid);
}
