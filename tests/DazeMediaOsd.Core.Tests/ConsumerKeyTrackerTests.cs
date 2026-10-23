using DazeMediaOsd.Core.Input;

namespace DazeMediaOsd.Core.Tests;

public class ConsumerKeyTrackerTests
{
    private const ushort Page = ConsumerKeyTracker.ConsumerPage;
    private static readonly (ushort, ushort)[] Released = [];

    [Fact]
    public void PressRaisesOnceWhileHeld()
    {
        var tracker = new ConsumerKeyTracker();

        Assert.Equal([MediaKey.VolumeUp], tracker.Update(1, [(Page, ConsumerKeyTracker.VolumeIncrement)]));
        Assert.Empty(tracker.Update(1, [(Page, ConsumerKeyTracker.VolumeIncrement)]));
        Assert.Empty(tracker.Update(1, Released));
        Assert.Equal([MediaKey.VolumeUp], tracker.Update(1, [(Page, ConsumerKeyTracker.VolumeIncrement)]));
    }

    [Fact]
    public void DevicesAreTrackedSeparately()
    {
        var tracker = new ConsumerKeyTracker();
        tracker.Update(1, [(Page, ConsumerKeyTracker.Mute)]);

        Assert.Equal([MediaKey.Mute], tracker.Update(2, [(Page, ConsumerKeyTracker.Mute)]));
    }

    [Fact]
    public void OtherUsagesAndPagesAreIgnored()
    {
        var tracker = new ConsumerKeyTracker();

        Assert.Empty(tracker.Update(1, [(Page, 0xB7)]));                                 // Stop
        Assert.Empty(tracker.Update(1, [(0x07, ConsumerKeyTracker.VolumeIncrement)]));   // keyboard page
    }

    [Theory]
    [InlineData(ConsumerKeyTracker.PlayPause, MediaKey.PlayPause)]
    [InlineData(ConsumerKeyTracker.Play, MediaKey.PlayPause)]
    [InlineData(ConsumerKeyTracker.Pause, MediaKey.PlayPause)]
    [InlineData(ConsumerKeyTracker.ScanNextTrack, MediaKey.NextTrack)]
    [InlineData(ConsumerKeyTracker.ScanPreviousTrack, MediaKey.PreviousTrack)]
    public void MediaUsagesRaiseMediaKeys(ushort usage, MediaKey expected) =>
        Assert.Equal([expected], new ConsumerKeyTracker().Update(1, [(Page, usage)]));

    [Fact]
    public void SecondKeyWhileFirstHeldRaisesOnlyTheNewOne()
    {
        var tracker = new ConsumerKeyTracker();
        tracker.Update(1, [(Page, ConsumerKeyTracker.VolumeDecrement)]);

        Assert.Equal([MediaKey.Mute],
            tracker.Update(1, [(Page, ConsumerKeyTracker.VolumeDecrement), (Page, ConsumerKeyTracker.Mute)]));
    }
}
