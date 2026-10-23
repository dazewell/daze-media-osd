using System.Text.Json.Serialization;

namespace DazeMediaOsd.Core.Settings;

/// <summary>Where the OSD sits in the work area of the primary display.</summary>
public enum OsdPosition
{
    BottomCenter,
    TopCenter,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

/// <summary>Persisted user settings. Immutable: change with <c>with</c> and <see cref="SettingsStore.Update"/>.</summary>
/// <param name="HideAfterSeconds">After the last show; the native flyout uses ~2.5 s (ARCHITECTURE → Native flyout).</param>
/// <param name="ShowInput">The microphone row.</param>
/// <param name="ShowMedia">The media row (while a session is active); off also stops media keys popping the OSD.</param>
/// <param name="ShowOnDeviceConnect">Pop the OSD when any audio device connects or disconnects (D-013).</param>
/// <param name="CycleOutputHotkey">Global shortcut that switches to the next output (<see cref="Input.Hotkey"/> text); "" is off.</param>
/// <param name="DeviceHotkeys">Shortcuts that each make one device the default (D-022); null reads as none.</param>
// Defaults on the constructor, not property initializers: System.Text.Json source-gen sets every init-only
// property, so a property missing from the file would come back as default(T) instead of our default (X-003).
// That also makes a new property backward compatible without a version bump.
public sealed record AppSettings(
    int Version = AppSettings.CurrentVersion,
    OsdPosition Position = OsdPosition.BottomCenter,
    double HideAfterSeconds = 2.5,
    bool ShowInput = true,
    bool ShowMedia = true,
    bool ShowOnDeviceConnect = true,
    string CycleOutputHotkey = AppSettings.DefaultCycleOutputHotkey,
    IReadOnlyList<DeviceHotkey>? DeviceHotkeys = null)
{
    public IReadOnlyList<DeviceHotkey> DeviceHotkeys { get; init; } = DeviceHotkeys ?? [];

    public const int CurrentVersion = 1;
    public const double MinHideAfterSeconds = 1;
    public const double MaxHideAfterSeconds = 10;
    /// <summary>Agreed with the user (2026-10-23); Win+Ctrl+V is Windows' own output picker.</summary>
    public const string DefaultCycleOutputHotkey = "Win+Alt+A";

    public static AppSettings Default { get; } = new();

    /// <summary>Clamps hand-edited or out-of-range values so a bad file can't break the OSD.</summary>
    public AppSettings Normalize() => this with
    {
        Version = CurrentVersion,
        Position = Enum.IsDefined(Position) ? Position : Default.Position,
        HideAfterSeconds = double.IsFinite(HideAfterSeconds)
            ? Math.Clamp(HideAfterSeconds, MinHideAfterSeconds, MaxHideAfterSeconds)
            : Default.HideAfterSeconds,
        CycleOutputHotkey = NormalizeHotkey(CycleOutputHotkey),
        DeviceHotkeys = NormalizeDeviceHotkeys(NormalizeHotkey(CycleOutputHotkey), DeviceHotkeys),
    };

    // A row without a valid chord stays (a device picked, keys not pressed yet), with "". One chord does one
    // thing: a chord the cycle shortcut or an earlier row already has is dropped from the later row.
    private static List<DeviceHotkey> NormalizeDeviceHotkeys(string cycle, IReadOnlyList<DeviceHotkey>? rows)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        if (cycle.Length > 0)
            used.Add(cycle);
        var result = new List<DeviceHotkey>();
        foreach (var row in rows ?? [])
        {
            if (row is null || !Enum.IsDefined(row.Flow))
                continue;
            string? text = row.Hotkey;
            var chord = Input.Hotkey.TryParse(text, out var hotkey) && used.Add(hotkey.ToString()) ? hotkey.ToString() : "";
            string? id = row.Id, name = row.Name; // null when the file says so
            result.Add(row with { Id = id ?? "", Name = name ?? "", Hotkey = chord });
        }
        return result;
    }

    // Records compare a list by reference; settings must compare by value, or SettingsStore.Update would
    // save and raise Changed for every no-op edit. A new setting goes into Equals and PrintMembers too.
    public bool Equals(AppSettings? other) =>
        other is not null
        && Version == other.Version
        && Position == other.Position
        && HideAfterSeconds.Equals(other.HideAfterSeconds)
        && ShowInput == other.ShowInput
        && ShowMedia == other.ShowMedia
        && ShowOnDeviceConnect == other.ShowOnDeviceConnect
        && CycleOutputHotkey == other.CycleOutputHotkey
        && DeviceHotkeys.SequenceEqual(other.DeviceHotkeys);

    // The log line ("settings changed: …") would otherwise print the list's type name.
    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append(System.Globalization.CultureInfo.InvariantCulture,
            $"Version = {Version}, Position = {Position}, HideAfterSeconds = {HideAfterSeconds}, ShowInput = {ShowInput}, ");
        builder.Append(System.Globalization.CultureInfo.InvariantCulture,
            $"ShowMedia = {ShowMedia}, ShowOnDeviceConnect = {ShowOnDeviceConnect}, CycleOutputHotkey = {CycleOutputHotkey}, ");
        builder.Append("DeviceHotkeys = [").AppendJoin(", ", DeviceHotkeys).Append(']');
        return true;
    }

    public override int GetHashCode() =>
        HashCode.Combine(Version, Position, HideAfterSeconds, ShowInput, ShowMedia, ShowOnDeviceConnect, CycleOutputHotkey, DeviceHotkeys.Count);

    // "" (off) stays off; a hand-edited typo falls back to the default rather than silently turning it off.
    private static string NormalizeHotkey(string? text) =>
        text is null ? DefaultCycleOutputHotkey
        : text.Length == 0 ? ""
        : Input.Hotkey.TryParse(text, out var hotkey) ? hotkey.ToString()
        : DefaultCycleOutputHotkey;
}

// Source-generated (no reflection): stays trim/AOT-safe (D-003).
[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
