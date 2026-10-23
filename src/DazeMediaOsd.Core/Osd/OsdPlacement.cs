using DazeMediaOsd.Core.Settings;

namespace DazeMediaOsd.Core.Osd;

public readonly record struct PixelRect(int X, int Y, int Width, int Height);

/// <summary>Where the OSD window goes inside a work area, in physical pixels.</summary>
public static class OsdPlacement
{
    public static (int X, int Y) Place(PixelRect workArea, int width, int height, OsdPosition position, int margin)
    {
        var left = workArea.X + margin;
        var right = workArea.X + workArea.Width - width - margin;
        var centre = workArea.X + (workArea.Width - width) / 2;
        var top = workArea.Y + margin;
        var bottom = workArea.Y + workArea.Height - height - margin;
        return position switch
        {
            OsdPosition.TopCenter => (centre, top),
            OsdPosition.TopLeft => (left, top),
            OsdPosition.TopRight => (right, top),
            OsdPosition.BottomLeft => (left, bottom),
            OsdPosition.BottomRight => (right, bottom),
            _ => (centre, bottom),
        };
    }
}
