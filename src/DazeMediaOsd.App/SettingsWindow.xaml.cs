using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using DazeMediaOsd.App.ViewModels;
using DazeMediaOsd.Core.Diagnostics;
using DazeMediaOsd.Core.Lifecycle;
using DazeMediaOsd.Core.Settings;
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

    public SettingsWindow(SettingsViewModel viewModel)
    {
        ViewModel = viewModel;
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
        BuiltWithText.Text = $"{RuntimeInformation.FrameworkDescription} · Windows App SDK {WindowsAppSdkVersion()}"
            + " · CommunityToolkit.Mvvm · CsWin32";

        Handle = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        var scale = OverlayWindow.Scale(Handle);
        AppWindow.Resize(new SizeInt32((int)(WidthDip * scale), (int)(HeightDip * scale)));
        Closed += (_, _) => ViewModel.EndRecording();
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
        Clipboard.SetContent(package);
        CopyDetailsIcon.Glyph = "\uE73E"; // CheckMark
        CopyDetailsText.Text = "Copied";
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

    private void OnOpenAppFolder(object sender, RoutedEventArgs e) => OpenInExplorer(AppContext.BaseDirectory);

    private void OnOpenLog(object sender, RoutedEventArgs e)
    {
        var log = Path.Combine(Log.DefaultDirectory, "app.log");
        if (!File.Exists(log))
        {
            OpenInExplorer(SettingsStore.DefaultDirectory);
            return;
        }
        using var _ = Process.Start(new ProcessStartInfo(log) { UseShellExecute = true }); // default .log editor
    }

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var tag = (args.SelectedItem as NavigationViewItem)?.Tag as string;
        OsdPage.Visibility = tag is "Osd" or null ? Visibility.Visible : Visibility.Collapsed;
        ShortcutsPage.Visibility = tag == "Shortcuts" ? Visibility.Visible : Visibility.Collapsed;
        AboutPage.Visibility = tag == "About" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnOpenDataFolder(object sender, RoutedEventArgs e) => OpenInExplorer(SettingsStore.DefaultDirectory);

    private static void OpenInExplorer(string folder)
    {
        Directory.CreateDirectory(folder);
        using var _ = Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder.TrimEnd(Path.DirectorySeparatorChar)}\""));
    }
}
