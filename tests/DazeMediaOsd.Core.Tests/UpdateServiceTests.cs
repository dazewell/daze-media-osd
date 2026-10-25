using DazeMediaOsd.Core.Updates;

namespace DazeMediaOsd.Core.Tests;

public sealed class UpdateServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NotInstalledNeverChecks()
    {
        var client = new FakeUpdateClient { IsInstalled = false };
        using var service = new UpdateService(client, new FixedTime());

        _ = service.CheckAsync();

        Assert.Equal(UpdateState.NotInstalled, service.Status.State);
        Assert.Equal(0, client.Finds);
    }

    [Fact]
    public void PendingDownloadStartsReady()
    {
        using var service = new UpdateService(new FakeUpdateClient { PendingVersion = "0.4.0" }, new FixedTime());

        Assert.Equal(new UpdateStatus(UpdateState.Ready, "0.4.0"), service.Status);
    }

    [Fact]
    public async Task NothingNewerIsUpToDate()
    {
        using var service = new UpdateService(new FakeUpdateClient(), new FixedTime());

        await service.CheckAsync();

        Assert.Equal(UpdateState.UpToDate, service.Status.State);
        Assert.Equal(Now, service.Status.CheckedAt);
    }

    [Fact]
    public async Task NewerReleaseIsDownloadedAndReady()
    {
        var client = new FakeUpdateClient { Found = "0.4.0" };
        using var service = new UpdateService(client, new FixedTime());
        var seen = new List<UpdateState>();
        service.StatusChanged += (_, s) => seen.Add(s.State);

        await service.CheckAsync();

        Assert.Equal([UpdateState.Checking, UpdateState.Downloading, UpdateState.Ready], seen);
        Assert.Equal(new UpdateStatus(UpdateState.Ready, "0.4.0", Now), service.Status);
        Assert.Equal(1, client.Downloads);
    }

    [Fact]
    public async Task FailureWithoutDownloadIsFailed()
    {
        using var service = new UpdateService(new FakeUpdateClient { Throw = true }, new FixedTime());

        await service.CheckAsync();

        Assert.Equal(UpdateState.Failed, service.Status.State);
    }

    [Fact]
    public async Task FailureKeepsADownloadedUpdateReady()
    {
        using var service = new UpdateService(new FakeUpdateClient { PendingVersion = "0.4.0", Throw = true }, new FixedTime());

        await service.CheckAsync();

        Assert.Equal(UpdateState.Ready, service.Status.State);
        Assert.Equal("0.4.0", service.Status.Version);
    }

    [Fact]
    public async Task NothingNewerKeepsADownloadedUpdateReady()
    {
        using var service = new UpdateService(new FakeUpdateClient { PendingVersion = "0.4.0" }, new FixedTime());

        await service.CheckAsync();

        Assert.Equal(new UpdateStatus(UpdateState.Ready, "0.4.0", Now), service.Status);
    }

    [Fact]
    public async Task OneCheckAtATime()
    {
        var gate = new TaskCompletionSource<string?>();
        var client = new FakeUpdateClient { FindTask = gate.Task };
        using var service = new UpdateService(client, new FixedTime());

        var first = service.CheckAsync();
        await service.CheckAsync(); // returns at once: the first is still running
        gate.SetResult(null);
        await first;

        Assert.Equal(1, client.Finds);
    }

    [Fact]
    public void ApplyOnlyWhenReady()
    {
        var notReady = new FakeUpdateClient();
        using var idle = new UpdateService(notReady, new FixedTime());
        var ready = new FakeUpdateClient { PendingVersion = "0.4.0" };
        using var pending = new UpdateService(ready, new FixedTime());

        Assert.False(idle.ApplyAfterExit());
        Assert.True(pending.ApplyAfterExit());
        Assert.Equal(0, notReady.Applies);
        Assert.Equal(1, ready.Applies);
    }

    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class FakeUpdateClient : IUpdateClient
    {
        public bool IsInstalled { get; set; } = true;

        public string? PendingVersion { get; set; }

        public string? Found { get; set; }

        public Task<string?>? FindTask { get; set; }

        public bool Throw { get; set; }

        public int Finds { get; private set; }

        public int Downloads { get; private set; }

        public int Applies { get; private set; }

        public Task<string?> FindAsync(CancellationToken cancellationToken)
        {
            Finds++;
            if (Throw)
                throw new HttpRequestException("offline");
            return FindTask ?? Task.FromResult(Found);
        }

        public Task DownloadAsync(CancellationToken cancellationToken)
        {
            Downloads++;
            PendingVersion = Found;
            return Task.CompletedTask;
        }

        public void ApplyAfterExit() => Applies++;
    }
}
