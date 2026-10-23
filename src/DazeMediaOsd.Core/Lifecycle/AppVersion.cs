using System.Reflection;

namespace DazeMediaOsd.Core.Lifecycle;

/// <summary>
/// The running build: <c>&lt;Version&gt;</c> from Directory.Build.props plus the commit the SDK appends
/// to the informational version (D-024).
/// </summary>
public sealed record AppVersion(string Number, string? Commit)
{
    private const int ShortCommitLength = 7;

    public static AppVersion Current { get; } = Parse(
        (Assembly.GetEntryAssembly() ?? typeof(AppVersion).Assembly)
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    /// <summary>The first seven characters of the commit: enough to find the build in git.</summary>
    public string? ShortCommit => Commit?[..Math.Min(ShortCommitLength, Commit.Length)];

    /// <summary>"0.1.0 (abc1234)", or just the number when the build carries no commit.</summary>
    public string Display => ShortCommit is null ? Number : $"{Number} ({ShortCommit})";

    /// <summary>"0.1.0+abc1234": the form the log and copied details use.</summary>
    public override string ToString() => ShortCommit is null ? Number : $"{Number}+{ShortCommit}";

    public static AppVersion Parse(string? informational)
    {
        if (string.IsNullOrWhiteSpace(informational))
            return new AppVersion("unknown", null);
        var plus = informational.IndexOf('+', StringComparison.Ordinal);
        if (plus < 0)
            return new AppVersion(informational, null);
        var commit = informational[(plus + 1)..];
        return new AppVersion(informational[..plus], commit.Length == 0 ? null : commit);
    }
}
