using DazeMediaOsd.Core.Diagnostics;
using Velopack;
using Velopack.Sources;

namespace DazeMediaOsd.Core.Updates;

/// <summary>Velopack against this repo's GitHub Releases (D-029, D-030). Stable releases only, no token.</summary>
public sealed class VelopackUpdateClient : IUpdateClient
{
    public const string RepoUrl = "https://github.com/dazewell/daze-media-osd";

    /// <summary>A local feed folder (vpk pack output) in place of GitHub, for testing updates (build skill).</summary>
    public const string SourceOverrideVariable = "DAZEMEDIAOSD_UPDATE_SOURCE";

    private readonly UpdateManager _manager = CreateManager();
    private UpdateInfo? _found;

    public bool IsInstalled => _manager.IsInstalled;

    public string? PendingVersion => _manager.UpdatePendingRestart?.Version.ToString();

    public async Task<string?> FindAsync(CancellationToken cancellationToken)
    {
        _found = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
        return _found?.TargetFullRelease.Version.ToString();
    }

    public Task DownloadAsync(CancellationToken cancellationToken) =>
        _found is null
            ? throw new InvalidOperationException("Nothing found to download.")
            : _manager.DownloadUpdatesAsync(_found, progress: null, cancellationToken);

    // No restart args: the updater starts the exe bare, i.e. the launcher (D-006).
    public void ApplyAfterExit() =>
        _manager.WaitExitThenApplyUpdates(_manager.UpdatePendingRestart, silent: true, restart: true, restartArgs: []);

    /// <summary>
    /// At launcher start: hands a downloaded update to the updater and exits this process, which the
    /// updater restarts once applied. Returns when there is nothing to apply. Skipped right after an
    /// update restart, so a failed apply can't loop.
    /// </summary>
    public static void ApplyPendingAtStart(bool restartedByUpdater)
    {
        if (restartedByUpdater)
            return;
        var manager = CreateManager();
        if (!manager.IsInstalled || manager.UpdatePendingRestart is not { } pending)
            return;
        Log.Info($"launcher: applying downloaded update {pending.Version}");
        manager.WaitExitThenApplyUpdates(pending, silent: true, restart: true, restartArgs: []);
        Environment.Exit(0);
    }

    private static UpdateManager CreateManager() =>
        Environment.GetEnvironmentVariable(SourceOverrideVariable) is { Length: > 0 } feed
            ? new UpdateManager(feed)
            : new UpdateManager(new GithubSource(RepoUrl, accessToken: null, prerelease: false));
}
