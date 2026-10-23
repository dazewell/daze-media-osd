using System.Text.Json;
using DazeMediaOsd.Core.Diagnostics;

namespace DazeMediaOsd.Core.Settings;

/// <summary>
/// <c>settings.json</c> in the app's data folder (D-003). Loads once, saves on every <see cref="Update"/>.
/// A missing or unreadable file means defaults; an unparsable one is kept aside as
/// <c>settings.invalid.json</c> instead of being silently overwritten. Never throws on I/O: a failed save
/// keeps the change for this session and reports it in <see cref="SaveError"/>.
/// </summary>
public sealed class SettingsStore
{
    private readonly Lock _gate = new();
    private readonly string _path;

    public SettingsStore(string directory)
    {
        _path = Path.Combine(directory, "settings.json");
        Current = Load();
    }

    public static string DefaultDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DazeMediaOsd");

    public AppSettings Current { get; private set; }

    /// <summary>Why the last save failed, or null once a save succeeds. Read it after <see cref="Update"/>.</summary>
    public string? SaveError { get; private set; }

    /// <summary>Raised on the thread that called <see cref="Update"/>, only when something changed.</summary>
    public event EventHandler<AppSettings>? Changed;

    public void Update(Func<AppSettings, AppSettings> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        AppSettings next;
        lock (_gate)
        {
            next = change(Current).Normalize();
            if (next == Current)
                return;
            Current = next;
            Save(next);
        }
        Changed?.Invoke(this, next);
    }

    private AppSettings Load()
    {
        string json;
        try
        {
            json = File.ReadAllText(_path);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return AppSettings.Default;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Error($"settings {_path} unreadable, using defaults", e);
            return AppSettings.Default;
        }
        try
        {
            var settings = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings)
                ?? throw new JsonException("null document");
            if (settings.Version > AppSettings.CurrentVersion)
                Log.Info($"settings version {settings.Version} is newer than {AppSettings.CurrentVersion}; reading what we know");
            return settings.Normalize();
        }
        catch (JsonException e)
        {
            Log.Error($"settings {_path} invalid, using defaults", e);
            try
            {
                File.Copy(_path, Path.Combine(Path.GetDirectoryName(_path)!, "settings.invalid.json"), overwrite: true);
            }
            catch (Exception copy) when (copy is IOException or UnauthorizedAccessException)
            {
                Log.Error("could not keep the invalid settings file aside", copy);
            }
            return AppSettings.Default;
        }
    }

    // Write a temp file and move it over: a crash mid-write leaves the old file, not half a new one.
    private void Save(AppSettings settings)
    {
        var temp = _path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings));
            File.Move(temp, _path, overwrite: true);
            SaveError = null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Error($"settings {_path} not saved", e);
            SaveError = e.Message;
            try
            {
                File.Delete(temp);
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
                // Harmless: the next successful save overwrites it.
            }
        }
    }
}
