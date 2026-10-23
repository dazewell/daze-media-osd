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
// Defaults on the constructor, not property initializers: System.Text.Json source-gen sets every init-only
// property, so a property missing from the file would come back as default(T) instead of our default (X-003).
// That also makes a new property backward compatible without a version bump.
public sealed record AppSettings(
    int Version = AppSettings.CurrentVersion,
    OsdPosition Position = OsdPosition.BottomCenter,
    double HideAfterSeconds = 2.5,
    bool ShowInput = true,
    bool ShowMedia = true,
    bool ShowOnDeviceConnect = true)
{
    public const int CurrentVersion = 1;
    public const double MinHideAfterSeconds = 1;
    public const double MaxHideAfterSeconds = 10;

    public static AppSettings Default { get; } = new();

    /// <summary>Clamps hand-edited or out-of-range values so a bad file can't break the OSD.</summary>
    public AppSettings Normalize() => this with
    {
        Version = CurrentVersion,
        Position = Enum.IsDefined(Position) ? Position : Default.Position,
        HideAfterSeconds = double.IsFinite(HideAfterSeconds)
            ? Math.Clamp(HideAfterSeconds, MinHideAfterSeconds, MaxHideAfterSeconds)
            : Default.HideAfterSeconds,
    };
}

// Source-generated (no reflection): stays trim/AOT-safe (D-003).
[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
