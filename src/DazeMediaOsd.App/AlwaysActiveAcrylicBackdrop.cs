using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace DazeMediaOsd.App;

/// <summary>
/// Desktop acrylic that stays acrylic: the stock <c>DesktopAcrylicBackdrop</c> falls back to a solid colour
/// while its window is inactive, and the OSD is never active.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "The controller lives from target connect to disconnect, which disposes it.")]
internal sealed partial class AlwaysActiveAcrylicBackdrop : SystemBackdrop
{
    private DesktopAcrylicController? _controller;
    private SystemBackdropConfiguration? _configuration;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);
        _configuration = new SystemBackdropConfiguration { IsInputActive = true };
        if (xamlRoot.Content is FrameworkElement root)
        {
            _configuration.Theme = ToBackdropTheme(root.ActualTheme);
            root.ActualThemeChanged += (sender, _) =>
            {
                if (_configuration is not null)
                    _configuration.Theme = ToBackdropTheme(sender.ActualTheme);
            };
        }
        _controller = new DesktopAcrylicController();
        _controller.SetSystemBackdropConfiguration(_configuration);
        _controller.AddSystemBackdropTarget(connectedTarget);
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        base.OnTargetDisconnected(disconnectedTarget);
        _controller?.RemoveSystemBackdropTarget(disconnectedTarget);
        _controller?.Dispose();
        _controller = null;
        _configuration = null;
    }

    private static SystemBackdropTheme ToBackdropTheme(ElementTheme theme) => theme switch
    {
        ElementTheme.Dark => SystemBackdropTheme.Dark,
        ElementTheme.Light => SystemBackdropTheme.Light,
        _ => SystemBackdropTheme.Default,
    };
}
