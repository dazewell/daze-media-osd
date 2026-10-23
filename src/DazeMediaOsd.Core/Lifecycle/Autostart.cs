using Microsoft.Win32;

namespace DazeMediaOsd.Core.Lifecycle;

public enum AutostartState
{
    Off,
    On,

    /// <summary>Our Run value exists, but the user turned it off in Task Manager or Settings → Startup apps.</summary>
    DisabledByWindows,
}

/// <summary>What the registry says right now; <see cref="Path"/> is the exe our Run value starts, if any.</summary>
public sealed record AutostartStatus(AutostartState State, string? Path);

/// <summary>
/// "Start with Windows" as an HKCU Run value pointing at the launcher (D-003, D-006). The registry is
/// the only source of truth (D-015): nothing is stored in settings.json, so Task Manager edits show up.
/// </summary>
public sealed class Autostart
{
    public const string DefaultRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string DefaultApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    public const string ValueName = "DazeMediaOsd";

    private readonly string _runKey;
    private readonly string _approvedKey;

    public Autostart(string runKey = DefaultRunKey, string approvedKey = DefaultApprovedKey)
    {
        _runKey = runKey;
        _approvedKey = approvedKey;
    }

    public AutostartStatus Read()
    {
        using var run = Registry.CurrentUser.OpenSubKey(_runKey);
        if (run?.GetValue(ValueName) is not string command)
            return new AutostartStatus(AutostartState.Off, null);
        var path = command.Trim().Trim('"');
        using var approved = Registry.CurrentUser.OpenSubKey(_approvedKey);
        // Explorer writes 12 bytes; an odd first byte (03, 07) means turned off, even (02, 06) on. No value = on.
        var disabled = approved?.GetValue(ValueName) is byte[] { Length: > 0 } flags && (flags[0] & 1) == 1;
        return new AutostartStatus(disabled ? AutostartState.DisabledByWindows : AutostartState.On, path);
    }

    /// <summary>Points the Run value at <paramref name="exePath"/> (no args = launcher mode) and clears a Task Manager "disabled".</summary>
    public void Enable(string exePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exePath);
        using (var run = Registry.CurrentUser.CreateSubKey(_runKey))
            run.SetValue(ValueName, $"\"{exePath}\"", RegistryValueKind.String);
        DeleteValue(_approvedKey);
    }

    public void Disable()
    {
        DeleteValue(_runKey);
        DeleteValue(_approvedKey);
    }

    private static void DeleteValue(string keyPath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
