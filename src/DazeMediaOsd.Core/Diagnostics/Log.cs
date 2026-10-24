using System.Diagnostics;
using System.Globalization;

namespace DazeMediaOsd.Core.Diagnostics;

/// <summary>
/// Minimal append-only file log. A tray app has no console, so this is how an agent sees what the app
/// did. Thread-safe; before <see cref="Initialize"/> it writes to the debugger only. Rotated daily,
/// 7 days kept (<see cref="LogRotation"/>).
/// </summary>
public static class Log
{
    // Launcher and app append to the same file; File.AppendAllText shares it read-only, so serialize
    // across processes (and threads) instead of losing lines to sharing violations.
    private static readonly Mutex FileMutex = new(initiallyOwned: false, @"Local\DazeMediaOsd.Log");
    private static string? _directory;
    private static string? _path;
    private static DateTime _rotatedDay; // guarded by FileMutex

    public static string DefaultDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DazeMediaOsd", "logs");

    /// <summary>Starts writing to <c>app.log</c> in <paramref name="directory"/>; stays debugger-only if it can't.</summary>
    public static void Initialize(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            _directory = directory;
            _path = Path.Combine(directory, LogRotation.FileName);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Log disabled, {directory} not writable: {e.Message}");
        }
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception exception) => Write("ERROR", $"{message}: {exception}");

    private static void Write(string level, string message)
    {
        var now = DateTime.Now;
        var line = string.Create(CultureInfo.InvariantCulture,
            $"{now:yyyy-MM-dd HH:mm:ss.fff} [{Environment.ProcessId}] {level} {message}");
        Debug.WriteLine(line);
        if (_path is null)
            return;
        var owned = false;
        try
        {
            try
            {
                owned = FileMutex.WaitOne(TimeSpan.FromSeconds(1));
            }
            catch (AbandonedMutexException)
            {
                owned = true; // the other process died mid-write; the mutex is ours now
            }
            if (!owned)
                return; // another writer is stuck: drop the line rather than collide (debugger still has it)
            // Read the clock again: the wait above may have crossed midnight.
            var rotateNow = DateTime.Now;
            if (_rotatedDay != rotateNow.Date)
            {
                LogRotation.Rotate(_directory!, rotateNow);
                _rotatedDay = rotateNow.Date;
            }
            File.AppendAllText(_path, line + Environment.NewLine);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Logging must never take the app down; it is called from audio and input callbacks.
        }
        finally
        {
            if (owned)
                FileMutex.ReleaseMutex();
        }
    }
}
