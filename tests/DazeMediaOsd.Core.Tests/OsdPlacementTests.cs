using DazeMediaOsd.Core.Osd;
using DazeMediaOsd.Core.Settings;

namespace DazeMediaOsd.Core.Tests;

public class OsdPlacementTests
{
    // A 1000×800 work area that doesn't start at the origin (taskbar on top, or a secondary monitor).
    private static readonly PixelRect Area = new(100, 50, 1000, 800);

    [Theory]
    [InlineData(OsdPosition.BottomCenter, 500, 740)]
    [InlineData(OsdPosition.TopCenter, 500, 60)]
    [InlineData(OsdPosition.TopLeft, 110, 60)]
    [InlineData(OsdPosition.TopRight, 890, 60)]
    [InlineData(OsdPosition.BottomLeft, 110, 740)]
    [InlineData(OsdPosition.BottomRight, 890, 740)]
    public void PlacesInsideTheWorkAreaWithMargin(OsdPosition position, int x, int y) =>
        Assert.Equal((x, y), OsdPlacement.Place(Area, 200, 100, position, 10));
}
