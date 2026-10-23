using System.Diagnostics;
using System.Reflection;
using DazeMediaOsd.App.ViewModels;
using DazeMediaOsd.Core.Settings;
using DazeMediaOsd.Core.Windowing;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
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
        VersionText.Text = "Version " + ShortVersion();

        Handle = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        var scale = OverlayWindow.Scale(Handle);
        AppWindow.Resize(new SizeInt32((int)(WidthDip * scale), (int)(HeightDip * scale)));
    }

    public SettingsViewModel ViewModel { get; }

    public nint Handle { get; }

    // The SDK appends "+<full commit sha>"; seven characters identify the build.
    private static string ShortVersion()
    {
        var version = typeof(SettingsWindow).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
        var plus = version.IndexOf('+', StringComparison.Ordinal);
        return plus >= 0 && version.Length > plus + 8 ? version[..(plus + 8)] : version;
    }

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var about = (args.SelectedItem as NavigationViewItem)?.Tag as string == "About";
        OsdPage.Visibility = about ? Visibility.Collapsed : Visibility.Visible;
        AboutPage.Visibility = about ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnOpenDataFolder(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(SettingsStore.DefaultDirectory);
        using var _ = Process.Start(new ProcessStartInfo("explorer.exe", $"\"{SettingsStore.DefaultDirectory}\""));
    }
}
