using System.Globalization;
using DazeMediaOsd.Core.Audio;
using DazeMediaOsd.Core.Osd;
using DazeMediaOsd.Core.Windowing;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Foundation;

namespace DazeMediaOsd.App;

/// <summary>The OSD: created once, hidden between shows, never activated.</summary>
public sealed partial class OsdWindow : Window
{
    // The native flyout hides ~2.5 s after the last key (ARCHITECTURE → Native flyout). Settings later.
    private static readonly TimeSpan HideAfter = TimeSpan.FromSeconds(2.5);
    private const double BottomMarginDip = 24;

    private readonly nint _hwnd;
    private readonly DispatcherQueueTimer _hideTimer;

    public OsdWindow()
    {
        InitializeComponent();
        SystemBackdrop = new AlwaysActiveAcrylicBackdrop();
        AppWindow.IsShownInSwitchers = false;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            // Border without title bar keeps the Win11 frame: rounded corners and the thin outline.
            presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }
        _hwnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        OverlayWindow.Apply(_hwnd);

        _hideTimer = DispatcherQueue.CreateTimer();
        _hideTimer.Interval = HideAfter;
        _hideTimer.IsRepeating = false;
        _hideTimer.Tick += (_, _) => OverlayWindow.Hide(_hwnd);
    }

    /// <summary>Renders <paramref name="state"/> and (re)starts the auto-hide countdown. UI thread only.</summary>
    public void Show(OsdState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        RenderOutput(state.Output);
        RenderInput(state.Input);
        Place();
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    private void RenderOutput(EndpointState? output)
    {
        OutputName.Text = output?.Name ?? "No output device";
        var percent = output?.Percent ?? 0;
        OutputLevel.Value = percent;
        OutputLevel.Opacity = output is { Muted: false } ? 1 : 0.4;
        OutputPercent.Text = output is null ? "" : percent.ToString(CultureInfo.CurrentCulture);
        OutputIcon.Glyph = output switch
        {
            null or { Muted: true } => "", // Mute
            { Percent: 0 } => "",           // Volume0
            { Percent: < 34 } => "",        // Volume1
            { Percent: < 67 } => "",        // Volume2
            _ => "",                        // Volume3
        };
    }

    private void RenderInput(EndpointState? input)
    {
        InputRow.Visibility = input is null ? Visibility.Collapsed : Visibility.Visible;
        if (input is null)
            return;
        InputName.Text = input.Name;
        InputIcon.Glyph = input.Muted ? "" : ""; // MicOff / Microphone
        InputStatus.Text = input.Muted ? "Muted" : input.Percent.ToString(CultureInfo.CurrentCulture);
    }

    // Bottom centre of the primary work area, like the native flyout.
    private void Place()
    {
        var scale = OverlayWindow.Scale(_hwnd);
        Root.Measure(new Size(Root.Width, double.PositiveInfinity));
        var width = (int)Math.Ceiling(Root.Width * scale);
        var height = (int)Math.Ceiling(Root.DesiredSize.Height * scale);
        var area = DisplayArea.Primary.WorkArea;
        OverlayWindow.ShowAt(_hwnd,
            area.X + (area.Width - width) / 2,
            area.Y + area.Height - height - (int)Math.Round(BottomMarginDip * scale),
            width, height);
    }
}
