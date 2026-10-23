namespace DazeMediaOsd.Core.NativeFlyout;

/// <summary>Identifies the native volume flyout host among explorer's windows (ARCHITECTURE → Native flyout).</summary>
internal static class FlyoutHostMatcher
{
    internal const string HostClass = "XamlExplorerHostIslandWindow";

    // Explorer has several hosts of the same class ("Task Switching" is band 16); the band tells them apart.
    internal const uint AboveLockUxBand = 18;

    internal static bool IsFlyoutHost(string className, string processName, uint band) =>
        className == HostClass
        && string.Equals(processName, "explorer", StringComparison.OrdinalIgnoreCase)
        && band == AboveLockUxBand;
}
