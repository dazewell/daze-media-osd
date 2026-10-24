using DazeMediaOsd.Core.Lifecycle;

namespace DazeMediaOsd.Core.Tests;

public class AppVersionTests
{
    [Fact]
    public void SplitsNumberAndCommit()
    {
        var version = AppVersion.Parse("0.1.0+0123456789abcdef0123456789abcdef01234567");
        Assert.Equal("0.1.0", version.Number);
        Assert.Equal("0123456", version.ShortCommit);
        Assert.Equal("0.1.0 (0123456)", version.Display);
        Assert.Equal("0.1.0+0123456", version.ToString());
    }

    [Theory]
    [InlineData("0.1.0")]
    [InlineData("0.1.0+")]
    public void WithoutCommitShowsTheNumberOnly(string informational)
    {
        var version = AppVersion.Parse(informational);
        Assert.Null(version.Commit);
        Assert.Equal("0.1.0", version.Display);
        Assert.Equal("0.1.0", version.ToString());
    }

    [Fact]
    public void KeepsAPrereleaseTag() =>
        Assert.Equal("0.2.0-beta.1 (abc1234)", AppVersion.Parse("0.2.0-beta.1+abc1234def").Display);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void MissingIsUnknown(string? informational) =>
        Assert.Equal("unknown", AppVersion.Parse(informational).Display);

    [Fact]
    public void CurrentComesFromTheBuild() =>
        Assert.NotEqual("unknown", AppVersion.Current.Number);
}
