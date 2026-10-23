using System.Diagnostics;
using System.Globalization;

namespace DazeMediaOsd.Core.Diagnostics;

/// <summary>
/// Minimal append-only file log. A tray app has no console, so this is how an agent sees what the app
/// did. Thread-safe; before <see cref="Initialize"/> it writes to the debugger only.
/// </summary>
public static class Log
{
    private static readonly Lock Gate = new();
    private static string? _path;

    public static string DefaultDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DazeMediaOsd", "logs");

    public static void Initialize(string directory)
    {
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "app.log");
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception exception) => Write("ERROR", $"{message}: {exception}");

    private static void Write(string level, string message)
    {
        var line = string.Create(CultureInfo.InvariantCulture,
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{Environment.ProcessId}] {level} {message}");
        Debug.WriteLine(line);
        if (_path is null)
            return;
        lock (Gate)
        {
            try
            {
                File.AppendAllText(_path, line + Environment.NewLine);
            }
            catch (IOException)
            {
                // Logging must never take the app down.
            }
        }
    }
}
