using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using DazeMediaOsd.Core.Audio;
using DazeMediaOsd.Core.Diagnostics;
using DazeMediaOsd.Core.Media;
using DazeMediaOsd.Core.Osd;
using DazeMediaOsd.Core.Settings;
using DazeMediaOsd.Core.Windowing;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;

namespace DazeMediaOsd.App;

/// <summary>The OSD: created once, hidden between shows, never activated.</summary>
public sealed partial class OsdWindow : Window
{
    private const double MarginDip = 24;
    // Fade + short slide from the nearest screen edge, close to the native flyout's feel.
    private const double SlideDip = 16;
    private static readonly TimeSpan ShowDuration = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan HideDuration = TimeSpan.FromMilliseconds(150);
    // Shows start one frame in, so the first composed frame is already visible (no later than unanimated).
    private static readonly TimeSpan FirstFrameLead = TimeSpan.FromMilliseconds(16);

    private readonly nint _hwnd;
    private readonly DispatcherQueueTimer _hideTimer;
    private AppSettings _settings = AppSettings.Default;
    private int _outputPercent;
    private byte[]? _thumbnail;
    private Storyboard? _eventEntrance;
    private readonly OsdAnimation _animation = new(ShowDuration, HideDuration);
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private bool _ticking;
    private (int X, int Y, int Width, int Height) _bounds;

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
        _hideTimer.Tick += (_, _) =>
        {
            _animation.Hide(_clock.Elapsed, OverlayWindow.AnimationsEnabled());
            Animate();
        };
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
        // Before Render, so the first frame is already the animation's first frame. A no-op while shown.
        // The lead only from hidden: a reversal mid-fade continues from the current value, without a jump.
        var now = _clock.Elapsed;
        _animation.Show(_animation.IsHidden ? now - FirstFrameLead : now, OverlayWindow.AnimationsEnabled());
        _animation.Tick(now);
        RenderDevice(state.Device);
        Render(state);
        Animate();
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    /// <summary>
    /// Re-renders while visible, leaving the hide countdown alone; ignored while hidden. UI thread only.
    /// Keeps the device event line: it belongs to the show, and a disconnect often pauses the player.
    /// </summary>
    public void Refresh(OsdState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!_animation.IsHidden)
            Render(state);
    }

    private void Render(OsdState state)
    {
        RenderMedia(state.Media);
        RenderOutput(state.Output);
        RenderInput(state.Input);
        Place();
    }

    // Only the show that announces a device has one; every other show clears the line (D-014).
    private void RenderDevice(DeviceNotice? device)
    {
        // Explicitly fully visible: with "Animation effects" now off, no entrance runs to set it.
        _eventEntrance?.Stop();
        EventRow.Opacity = 1;
        EventIconScale.ScaleX = EventIconScale.ScaleY = 1;
        if (device is null)
        {
            EventRow.Visibility = Visibility.Collapsed;
            return;
        }
        EventRow.Visibility = Visibility.Visible;
        var name = device.Name.Length == 0 ? "Audio device" : device.Name;
        EventName.Text = device.Others == 0
            ? name
            : string.Create(CultureInfo.CurrentCulture, $"{name} +{device.Others}");
        EventIcon.Glyph = device switch
        {
            { Bluetooth: true } => "", // Bluetooth
            _ when KindGlyph(device.Kind) is { } glyph => glyph,
            { Flow: EndpointFlow.Input } => "", // Microphone
            _ => "", // Speakers
        };
        EventIcon.Style = (Style)Root.Resources[device.Connected ? "OsdEventIcon" : "OsdEventIconGone"];
        EventStatus.Style = (Style)Root.Resources[device.Connected ? "OsdEventStatus" : "OsdEventStatusGone"];
        EventStatus.Text = device.Connected ? "Connected" : "Disconnected";
        if (OverlayWindow.AnimationsEnabled())
            AnimateEventEntrance();
    }

    // The line fades in and its glyph settles from a smaller scale; independent animations, so they run on
    // the composition thread. Off with Windows "Animation effects" (D-012).
    private void AnimateEventEntrance()
    {
        if (_eventEntrance is null)
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            _eventEntrance = new Storyboard();
            Add(EventRow, "Opacity", 0, 1, 250);
            Add(EventIconScale, "ScaleX", 0.6, 1, 350);
            Add(EventIconScale, "ScaleY", 0.6, 1, 350);

            void Add(DependencyObject target, string property, double from, double to, int ms)
            {
                var animation = new DoubleAnimation
                {
                    From = from,
                    To = to,
                    Duration = TimeSpan.FromMilliseconds(ms),
                    EasingFunction = ease,
                };
                Storyboard.SetTarget(animation, target);
                Storyboard.SetTargetProperty(animation, property);
                _eventEntrance.Children.Add(animation);
            }
        }
        _eventEntrance.Begin();
    }

    private void RenderMedia(MediaState? media)
    {
        if (media is null || !_settings.ShowMedia)
        {
            MediaRow.Visibility = Visibility.Collapsed;
            return;
        }
        MediaRow.Visibility = Visibility.Visible;
        MediaTitle.Text = media.Title;
        MediaArtist.Text = media.Artist;
        MediaArtist.Visibility = media.Artist.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        MediaPlayback.Glyph = media.IsPlaying ? "" : ""; // Play / Pause
        RenderThumbnail(media.Thumbnail);
    }

    // The service keeps the same array while the cover is unchanged, so this decodes once per cover.
    private async void RenderThumbnail(byte[]? bytes)
    {
        if (ReferenceEquals(bytes, _thumbnail))
            return;
        _thumbnail = bytes;
        MediaThumbnail.Source = null;
        MediaPlaceholder.Visibility = Visibility.Visible;
        if (bytes is null)
            return;
        try
        {
            var image = new BitmapImage();
            using (var stream = new MemoryStream(bytes).AsRandomAccessStream())
                await image.SetSourceAsync(stream);
            if (!ReferenceEquals(bytes, _thumbnail))
                return; // a newer cover arrived while this one decoded
            MediaThumbnail.Source = image;
            MediaPlaceholder.Visibility = Visibility.Collapsed;
        }
        catch (Exception e) when (e is COMException or ArgumentException)
        {
            Log.Error($"media thumbnail ({bytes.Length} B) could not be decoded", e);
        }
    }

    private void OnThumbnailFailed(object sender, ExceptionRoutedEventArgs e)
    {
        MediaThumbnail.Source = null;
        MediaPlaceholder.Visibility = Visibility.Visible;
    }

    private void RenderOutput(EndpointState? output)
    {
        OutputName.Text = output?.Name ?? "No output device";
        _outputPercent = output?.Percent ?? 0;
        var muted = output is not { Muted: false };
        OutputFill.Style = (Style)Root.Resources[muted ? "OsdFillMuted" : "OsdFill"];
        OutputPercent.Style = (Style)Root.Resources[muted ? "OsdValueMuted" : "OsdValue"];
        OutputPercent.Text = output is null ? "" : _outputPercent.ToString(CultureInfo.CurrentCulture);
        // The device type where it says more than "speaker"; a speaker shows its level, as the native flyout
        // does. Mute wins over both: the state matters more than the type (D-017).
        OutputIcon.Glyph = output switch
        {
            null or { Muted: true } => "\uE74F", // Mute
            { Kind: DeviceKind.Headphones or DeviceKind.Headset or DeviceKind.Display } => KindGlyph(output.Kind)!,
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

    private static string? KindGlyph(DeviceKind kind) => kind switch
    {
        DeviceKind.Speakers => "",   // Speakers
        DeviceKind.Headphones => "", // Headphone
        DeviceKind.Headset => "",    // Headset
        DeviceKind.Display => "",    // TVMonitor
        DeviceKind.Microphone => "", // Microphone
        _ => null,
    };

    private void RenderInput(EndpointState? input)
    {
        if (input is null || !_settings.ShowInput)
        {
            InputRow.Visibility = Visibility.Collapsed;
            return;
        }
        InputRow.Visibility = Visibility.Visible;
        InputName.Text = input.Name;
        InputIcon.Glyph = input.Muted ? "\uEC54" : input.Kind == DeviceKind.Headset ? "\uE95B" : "\uE720"; // MicOff / Headset / Microphone
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
        _bounds = (x, y, width, height);
        ApplyFrame();
    }

    // Steps the animation once per composition frame while it runs; idle otherwise.
    private void Animate()
    {
        if (_animation.IsRunning && !_ticking)
        {
            _ticking = true;
            CompositionTarget.Rendering += OnRendering;
        }
        else if (!_animation.IsRunning)
        {
            StopTicking();
            ApplyFrame();
        }
    }

    private void OnRendering(object? sender, object e)
    {
        if (!_animation.Tick(_clock.Elapsed))
            StopTicking();
        ApplyFrame();
    }

    private void StopTicking()
    {
        if (!_ticking)
            return;
        _ticking = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    // Opacity through the layered window's alpha and the slide through the window position: XAML/Composition
    // animations on Root wouldn't move the DWM frame, shadow and acrylic with it (D-012).
    private void ApplyFrame()
    {
        if (_animation.IsHidden)
        {
            OverlayWindow.Hide(_hwnd);
            return;
        }
        var visibility = _animation.Visibility;
        var (x, y, width, height) = _bounds;
        var fromTop = _settings.Position is OsdPosition.TopCenter or OsdPosition.TopLeft or OsdPosition.TopRight;
        var slide = (int)Math.Round((1 - visibility) * SlideDip * OverlayWindow.Scale(_hwnd));
        _ = OverlayWindow.SetAlpha(_hwnd, (byte)Math.Round(255 * visibility));
        if (!OverlayWindow.ShowAt(_hwnd, x, fromTop ? y - slide : y + slide, width, height))
            Log.Error($"OSD not shown at {x},{y} {width}x{height}", new Win32Exception());
    }
}
