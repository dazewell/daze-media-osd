using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using DazeMediaOsd.App.ViewModels;
using DazeMediaOsd.Core.Diagnostics;
using DazeMediaOsd.Core.Lifecycle;
using DazeMediaOsd.Core.Settings;
using DazeMediaOsd.Core.Updates;
using DazeMediaOsd.Core.Windowing;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;

namespace DazeMediaOsd.App;

/// <summary>Created when opened from the tray, destroyed on close (D-003). An ordinary, activatable window.</summary>
public sealed partial class SettingsWindow : Window
{
    private const int WidthDip = 860;
    private const int HeightDip = 620;

    private readonly UpdateService _updates;
    private readonly Action _restartToUpdate;

    /// <param name="restartToUpdate">Hands the ready update to the updater and exits the app.</param>
    public SettingsWindow(SettingsViewModel viewModel, UpdateService updates, Action restartToUpdate)
    {
        ArgumentNullException.ThrowIfNull(updates);
        ViewModel = viewModel;
        _updates = updates;
        _restartToUpdate = restartToUpdate;
        InitializeComponent();
        SystemBackdrop = new MicaBackdrop();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        HideAfterBox.Minimum = AppSettings.MinHideAfterSeconds;
        HideAfterBox.Maximum = AppSettings.MaxHideAfterSeconds;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico")); // taskbar, Alt+Tab
        // From code with a file path: an ms-appx IconSource in XAML blanks the whole window (X-006).
        AppTitleBar.IconSource = new ImageIconSource
        {
            ImageSource = new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "app.png"))),
        };
        VersionText.Text = "Version " + AppVersion.Current.Display;
        AppFolderText.Text = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        DataFolderText.Text = SettingsStore.DefaultDirectory;

        Handle = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        var scale = OverlayWindow.Scale(Handle);
        AppWindow.Resize(new SizeInt32((int)(WidthDip * scale), (int)(HeightDip * scale)));
        Closed += (_, _) =>
        {
            ViewModel.EndRecording();
            ViewModel.Dispose();
        };

        // Raised on the checking thread; the window may be gone by the time it lands.
        var ui = DispatcherQueue;
        void OnUpdateStatus(object? sender, UpdateStatus status) => ui.TryEnqueue(() => ShowUpdate(status));
        _updates.StatusChanged += OnUpdateStatus;
        Closed += (_, _) => _updates.StatusChanged -= OnUpdateStatus;
        ShowUpdate(_updates.Status);
    }

    public SettingsViewModel ViewModel { get; }

    public nint Handle { get; }

    private static string WindowsAppSdkVersion() => typeof(SettingsWindow).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "WindowsAppSdkVersion")?.Value ?? "(unknown)";

    private static string Details() => string.Join(Environment.NewLine,
        $"Daze Media OSD {AppVersion.Current}",
        $"Windows {Environment.OSVersion.Version} {RuntimeInformation.OSArchitecture}",
        $"{RuntimeInformation.FrameworkDescription}, Windows App SDK {WindowsAppSdkVersion()}",
        Environment.ProcessPath);

    private void OnCopyDetails(object sender, RoutedEventArgs e)
    {
        var package = new DataPackage();
        package.SetText(Details());
        try
        {
            Clipboard.SetContent(package);
            CopyDetailsIcon.Glyph = "\uE73E"; // CheckMark
            CopyDetailsText.Text = "Copied";
        }
        catch (COMException ex) // another app holds the clipboard open (CLIPBRD_E_CANT_OPEN)
        {
            Log.Error("copy details failed", ex);
            CopyDetailsIcon.Glyph = "\uE783"; // Error
            CopyDetailsText.Text = "Couldn't copy";
        }
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(2);
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            CopyDetailsIcon.Glyph = "\uE8C8"; // Copy
            CopyDetailsText.Text = "Copy details";
        };
        timer.Start();
    }

    private void ShowUpdate(UpdateStatus status)
    {
        UpdateTitle.Text = status.State == UpdateState.Ready ? "Update ready" : "Updates";
        UpdateText.Text = status.State switch
        {
            UpdateState.NotInstalled => "Only the installed app updates itself. This copy runs from a build folder.",
            UpdateState.Checking => "Checking for updates…",
            UpdateState.UpToDate => $"You're up to date. Last checked {When(status.CheckedAt)}",
            UpdateState.Downloading => $"Downloading version {status.Version}…",
            UpdateState.Ready => $"Version {status.Version} installs the next time the app starts.",
            UpdateState.Failed => "Couldn't check for updates. Check your internet connection, or see the log.",
            _ => "Checks for updates once a day",
        };
        UpdateButton.Visibility = status.State == UpdateState.NotInstalled ? Visibility.Collapsed : Visibility.Visible;
        UpdateButton.IsEnabled = status.State is not (UpdateState.Checking or UpdateState.Downloading);
        UpdateButton.Content = status.State switch
        {
            UpdateState.Ready => "Restart now",
            UpdateState.Failed => "Try again",
            _ => "Check for updates",
        };
    }

    private static string When(DateTimeOffset? at)
    {
        if (at is not { } time)
            return "";
        var local = time.ToLocalTime();
        return local.Date == DateTime.Today ? $"today at {local:t}" : $"{local:d} at {local:t}";
    }

    private void OnUpdateButton(object sender, RoutedEventArgs e)
    {
        if (_updates.Status.State == UpdateState.Ready)
            _restartToUpdate();
        else
            _ = _updates.CheckAsync();
    }

    private void OnOpenAppFolder(object sender, RoutedEventArgs e) => OpenInExplorer(AppContext.BaseDirectory);

    private void OnOpenLog(object sender, RoutedEventArgs e)
    {
        var log = Path.Combine(Log.DefaultDirectory, "app.log");
        if (!File.Exists(log))
        {
            OpenInExplorer(Log.DefaultDirectory);
            return;
        }
        using var _ = Process.Start(new ProcessStartInfo(log) { UseShellExecute = true }); // default .log editor
    }

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var tag = (args.SelectedItem as NavigationViewItem)?.Tag as string;
        OsdPage.Visibility = tag is "Osd" or null ? Visibility.Visible : Visibility.Collapsed;
        ShortcutsPage.Visibility = tag == "Shortcuts" ? Visibility.Visible : Visibility.Collapsed;
        WheelPage.Visibility = tag == "Wheel" ? Visibility.Visible : Visibility.Collapsed;
        AboutPage.Visibility = tag == "About" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnOpenDataFolder(object sender, RoutedEventArgs e) => OpenInExplorer(SettingsStore.DefaultDirectory);

    private static void OpenInExplorer(string folder)
    {
        Directory.CreateDirectory(folder);
        using var _ = Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder.TrimEnd(Path.DirectorySeparatorChar)}\""));
    }
}
