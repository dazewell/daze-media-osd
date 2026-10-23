using System.Diagnostics;
using System.Reflection;
using DazeMediaOsd.App.ViewModels;
using DazeMediaOsd.Core.Input;
using DazeMediaOsd.Core.Settings;
using DazeMediaOsd.Core.Windowing;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.System;
using Windows.UI.Core;

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

    private const string RecordingPrompt = "Press a shortcut";
    private bool _recorded; // since the box got focus: show the stored value, not the prompt, once keys are up

    // Records chords instead of taking text (D-021). Chords the shell keeps for itself never arrive here;
    // RegisterHotKey would refuse them anyway.
    private void OnHotkeyKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Tab)
            return; // keep keyboard navigation out of the box
        e.Handled = true;
        if (e.Key is VirtualKey.Back or VirtualKey.Delete)
        {
            ViewModel.ClearHotkey();
            ShowRecorded();
            return;
        }
        if (e.Key == VirtualKey.Escape)
        {
            ShowRecorded();
            return;
        }
        var modifiers = HeldModifiers();
        if (IsModifier(e.Key))
        {
            HotkeyBox.Text = Hotkey.Prefix(modifiers);
            return;
        }
        if (Hotkey.TryCreate(modifiers, (uint)e.Key, out var hotkey))
            ViewModel.CycleOutputHotkey = hotkey.ToString();
        else
            ViewModel.RejectHotkey();
        ShowRecorded();
    }

    private void OnHotkeyKeyUp(object sender, KeyRoutedEventArgs e)
    {
        e.Handled = true;
        if (HeldModifiers() == HotkeyModifiers.None)
            HotkeyBox.Text = _recorded ? ViewModel.CycleOutputHotkey : RecordingPrompt;
    }

    private void OnHotkeyGotFocus(object sender, RoutedEventArgs e)
    {
        ViewModel.PauseHotkey(true);
        _recorded = false;
        HotkeyBox.Text = RecordingPrompt;
    }

    private void OnHotkeyLostFocus(object sender, RoutedEventArgs e)
    {
        ViewModel.PauseHotkey(false);
        HotkeyBox.Text = ViewModel.CycleOutputHotkey;
    }

    private void OnResetHotkey(object sender, RoutedEventArgs e) => ViewModel.ResetHotkey();

    private void OnClearHotkey(object sender, RoutedEventArgs e) => ViewModel.ClearHotkey();

    // The binding doesn't refresh the box when the value is unchanged (same chord again).
    private void ShowRecorded()
    {
        _recorded = true;
        HotkeyBox.Text = ViewModel.CycleOutputHotkey;
    }

    private static bool IsModifier(VirtualKey key) => key is VirtualKey.Control or VirtualKey.LeftControl
        or VirtualKey.RightControl or VirtualKey.Menu or VirtualKey.LeftMenu or VirtualKey.RightMenu
        or VirtualKey.Shift or VirtualKey.LeftShift or VirtualKey.RightShift
        or VirtualKey.LeftWindows or VirtualKey.RightWindows;

    private static HotkeyModifiers HeldModifiers()
    {
        static bool Down(VirtualKey key) =>
            InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
        var modifiers = HotkeyModifiers.None;
        if (Down(VirtualKey.LeftWindows) || Down(VirtualKey.RightWindows))
            modifiers |= HotkeyModifiers.Win;
        if (Down(VirtualKey.Control))
            modifiers |= HotkeyModifiers.Ctrl;
        if (Down(VirtualKey.Menu))
            modifiers |= HotkeyModifiers.Alt;
        if (Down(VirtualKey.Shift))
            modifiers |= HotkeyModifiers.Shift;
        return modifiers;
    }

    private void OnOpenDataFolder(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(SettingsStore.DefaultDirectory);
        using var _ = Process.Start(new ProcessStartInfo("explorer.exe", $"\"{SettingsStore.DefaultDirectory}\""));
    }
}
