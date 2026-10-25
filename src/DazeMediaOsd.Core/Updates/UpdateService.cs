using DazeMediaOsd.Core.Diagnostics;

namespace DazeMediaOsd.Core.Updates;

public enum UpdateState
{
    /// <summary>Not a Velopack install (Debug or a hand-copied folder): nothing to update.</summary>
    NotInstalled,
    NotChecked,
    Checking,
    UpToDate,
    Downloading,

    /// <summary>Downloaded; applies the next time the launcher starts, or on Restart now.</summary>
    Ready,
    Failed,
}

/// <summary><see cref="Version"/>: the update found or downloaded. <see cref="CheckedAt"/>: last finished check.</summary>
public sealed record UpdateStatus(UpdateState State, string? Version = null, DateTimeOffset? CheckedAt = null);

/// <summary>The updater the service drives; Velopack in the app, a fake in tests.</summary>
public interface IUpdateClient
{
    bool IsInstalled { get; }

    /// <summary>A downloaded update newer than this copy, if any.</summary>
    string? PendingVersion { get; }

    /// <summary>The newest release if it's newer than this copy, else null. Remembers it for <see cref="DownloadAsync"/>.</summary>
    Task<string?> FindAsync(CancellationToken cancellationToken);

    Task DownloadAsync(CancellationToken cancellationToken);

    /// <summary>Starts the updater, which waits for this process to exit, applies the update and starts the launcher.</summary>
    void ApplyAfterExit();
}

/// <summary>
/// Checks GitHub Releases a minute after start and then daily, downloads a newer release in the background
/// and leaves it for the next launcher start to apply (D-030). One check at a time; failures are logged
/// and retried on the next tick. <see cref="StatusChanged"/> is raised on the checking thread.
/// </summary>
public sealed class UpdateService : IDisposable
{
    public static readonly TimeSpan FirstCheckDelay = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan CheckInterval = TimeSpan.FromDays(1);

    private readonly IUpdateClient _client;
    private readonly TimeProvider _time;
    private readonly CancellationTokenSource _stop = new();
    private ITimer? _timer;
    private int _checking;

    public UpdateService(IUpdateClient client, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
        _time = time ?? TimeProvider.System;
        Status = !client.IsInstalled ? new UpdateStatus(UpdateState.NotInstalled)
            : client.PendingVersion is { } pending ? new UpdateStatus(UpdateState.Ready, pending)
            : new UpdateStatus(UpdateState.NotChecked);
    }

    public event EventHandler<UpdateStatus>? StatusChanged;

    public UpdateStatus Status { get; private set; }

    /// <summary>Schedules the first and the daily checks. Does nothing outside an install.</summary>
    public void Start()
    {
        if (Status.State == UpdateState.NotInstalled || _timer is not null)
            return;
        _timer = _time.CreateTimer(_ => _ = CheckAsync(), null, FirstCheckDelay, CheckInterval);
    }

    /// <summary>Checks now (About's button, the timer). Returns at once if a check is already running.</summary>
    public async Task CheckAsync()
    {
        if (Status.State == UpdateState.NotInstalled || Interlocked.Exchange(ref _checking, 1) == 1)
            return;
        try
        {
            var previous = Status;
            Set(new UpdateStatus(UpdateState.Checking, previous.Version, previous.CheckedAt));
            var found = await _client.FindAsync(_stop.Token).ConfigureAwait(false);
            if (found is null)
            {
                // A downloaded update stays ready: GitHub just has nothing newer than it.
                Set(_client.PendingVersion is { } pending
                    ? new UpdateStatus(UpdateState.Ready, pending, _time.GetLocalNow())
                    : new UpdateStatus(UpdateState.UpToDate, null, _time.GetLocalNow()));
                Log.Info(Status.State == UpdateState.Ready ? $"updates: nothing newer, {Status.Version} still ready" : "updates: up to date");
                return;
            }
            Set(new UpdateStatus(UpdateState.Downloading, found, previous.CheckedAt));
            Log.Info($"updates: downloading {found}");
            await _client.DownloadAsync(_stop.Token).ConfigureAwait(false);
            Set(new UpdateStatus(UpdateState.Ready, found, _time.GetLocalNow()));
            Log.Info($"updates: {found} ready, applies on next start");
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
            // Disposed mid-check (app exiting).
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // Network, GitHub rate limit, disk: the daily tick tries again.
            Log.Error("updates: check failed", e);
            Set(_client.PendingVersion is { } pending
                ? new UpdateStatus(UpdateState.Ready, pending, Status.CheckedAt)
                : new UpdateStatus(UpdateState.Failed, null, _time.GetLocalNow()));
        }
        finally
        {
            Volatile.Write(ref _checking, 0);
        }
    }

    /// <summary>Hands a ready update to the updater; the caller then exits the app (tray Exit path).</summary>
    public bool ApplyAfterExit()
    {
        if (Status.State != UpdateState.Ready)
            return false;
        Log.Info($"updates: applying {Status.Version} after exit");
        _client.ApplyAfterExit();
        return true;
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _stop.Cancel();
        _stop.Dispose();
    }

    private void Set(UpdateStatus status)
    {
        Status = status;
        StatusChanged?.Invoke(this, status);
    }
}
