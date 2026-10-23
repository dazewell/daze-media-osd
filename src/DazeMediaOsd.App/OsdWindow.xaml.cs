using System.ComponentModel;
using System.Globalization;
using DazeMediaOsd.Core.Audio;
using DazeMediaOsd.Core.Diagnostics;
using DazeMediaOsd.Core.Osd;
using DazeMediaOsd.Core.Settings;
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
    private const double MarginDip = 24;

    private readonly nint _hwnd;
    private readonly DispatcherQueueTimer _hideTimer;
    private AppSettings _settings = AppSettings.Default;
    private int _outputPercent;

    public OsdWindow(AppSettings settings)
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
        _hideTimer.IsRepeating = false;
        Apply(settings);
        _hideTimer.Tick += (_, _) => OverlayWindow.Hide(_hwnd);
        // Before the first layout the track has no width yet.
        OutputTrack.SizeChanged += (_, _) => SizeFill();
    }

    /// <summary>Takes effect from the next <see cref="Show"/>. UI thread only.</summary>
    public void Apply(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        _hideTimer.Interval = TimeSpan.FromSeconds(settings.HideAfterSeconds);
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
        _outputPercent = output?.Percent ?? 0;
        var muted = output is not { Muted: false };
        OutputFill.Style = (Style)Root.Resources[muted ? "OsdFillMuted" : "OsdFill"];
        OutputPercent.Style = (Style)Root.Resources[muted ? "OsdValueMuted" : "OsdValue"];
        OutputPercent.Text = output is null ? "" : _outputPercent.ToString(CultureInfo.CurrentCulture);
        OutputIcon.Glyph = output switch
        {
            null or { Muted: true } => "\uE74F", // Mute
            { Percent: 0 } => "\uE992",           // Volume0
            { Percent: < 34 } => "\uE993",        // Volume1
            { Percent: < 67 } => "\uE994",        // Volume2
            _ => "\uE995",                        // Volume3
        };
        SizeFill();
    }

    // Set directly, no easing: a held key updates every 30–60 ms and any transition lags or restarts.
    // Nothing at 0 %, at least a round 4×4 cap above it.
    private void SizeFill()
    {
        var width = Math.Round(OutputTrack.ActualWidth * _outputPercent / 100);
        OutputFill.Visibility = _outputPercent == 0 ? Visibility.Collapsed : Visibility.Visible;
        OutputFill.Width = Math.Max(OutputTrack.Height, width);
    }

    private void RenderInput(EndpointState? input)
    {
        if (input is null || !_settings.ShowInput)
        {
            InputRow.Visibility = Visibility.Collapsed;
            return;
        }
        InputRow.Visibility = Visibility.Visible;
        InputName.Text = input.Name;
        InputIcon.Glyph = input.Muted ? "\uEC54" : "\uE720"; // MicOff / Microphone
        InputStatus.Style = (Style)Root.Resources[input.Muted ? "OsdValueMuted" : "OsdValue"];
        InputStatus.Text = input.Muted ? "Muted" : input.Percent.ToString(CultureInfo.CurrentCulture);
    }

    // In the primary work area at the chosen position (default bottom centre, like the native flyout). Root
    // sizes the client area; the native border sits outside it, else it clips Root's right and bottom edge.
    private void Place()
    {
        var scale = OverlayWindow.Scale(_hwnd);
        Root.Measure(new Size(Root.Width, double.PositiveInfinity));
        var (frameWidth, frameHeight) = OverlayWindow.FrameSize(_hwnd);
        var width = (int)Math.Ceiling(Root.Width * scale) + frameWidth;
        var height = (int)Math.Ceiling(Root.DesiredSize.Height * scale) + frameHeight;
        var area = DisplayArea.Primary.WorkArea;
        var (x, y) = OsdPlacement.Place(new PixelRect(area.X, area.Y, area.Width, area.Height), width, height,
            _settings.Position, (int)Math.Round(MarginDip * scale));
        if (!OverlayWindow.ShowAt(_hwnd, x, y, width, height))
            Log.Error($"OSD not shown at {x},{y} {width}x{height}", new Win32Exception());
    }
}
