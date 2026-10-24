using System.Diagnostics;
using System.Globalization;

namespace DazeMediaOsd.Core.Diagnostics;

/// <summary>
/// Daily rotation of <c>app.log</c> (D-028): the first write of a new day renames the file to
/// <c>app-yyyy-MM-dd.log</c> (the day it was last written) and deletes archives older than
/// <see cref="KeepDays"/> days. <c>app.log</c> is always today's. The caller holds the log mutex, so of
/// the two processes only the first to write on a new day rotates.
/// </summary>
internal static class LogRotation
{
    public const string FileName = "app.log";

    /// <summary>Days kept, today's <c>app.log</c> included.</summary>
    public const int KeepDays = 7;

    private const string ArchiveFormat = "yyyy-MM-dd";

    public static void Rotate(string directory, DateTime now)
    {
        var today = now.Date;
        var current = Path.Combine(directory, FileName);
        try
        {
            var lastDay = File.Exists(current) ? File.GetLastWriteTime(current).Date : today;
            if (lastDay < today)
                Archive(current, Path.Combine(directory, ArchiveName(lastDay)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Held open without delete sharing (a tail in a terminal): keep appending, retry tomorrow.
            Debug.WriteLine($"Log rotation skipped: {e.Message}");
        }

        foreach (var archive in Directory.GetFiles(directory, "app-*.log"))
        {
            if (ArchiveDay(Path.GetFileName(archive)) is { } day && day <= today.AddDays(-KeepDays))
                TryDelete(archive);
        }
    }

    internal static string ArchiveName(DateTime day) =>
        $"app-{day.ToString(ArchiveFormat, CultureInfo.InvariantCulture)}.log";

    private static DateTime? ArchiveDay(string fileName) =>
        DateTime.TryParseExact(fileName["app-".Length..^".log".Length], ArchiveFormat,
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : null;

    private static void Archive(string current, string archive)
    {
        if (!File.Exists(archive))
        {
            File.Move(current, archive);
            return;
        }
        // Same day archived before (clock turned back): keep both, in order.
        File.AppendAllText(archive, File.ReadAllText(current));
        File.Delete(current);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Old log not deleted: {path}: {e.Message}");
        }
    }
}
