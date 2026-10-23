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

        Assert.Equal([VolumeKey.Up], tracker.Update(1, [(Page, ConsumerKeyTracker.VolumeIncrement)]));
        Assert.Empty(tracker.Update(1, [(Page, ConsumerKeyTracker.VolumeIncrement)]));
        Assert.Empty(tracker.Update(1, Released));
        Assert.Equal([VolumeKey.Up], tracker.Update(1, [(Page, ConsumerKeyTracker.VolumeIncrement)]));
    }

    [Fact]
    public void DevicesAreTrackedSeparately()
    {
        var tracker = new ConsumerKeyTracker();
        tracker.Update(1, [(Page, ConsumerKeyTracker.Mute)]);

        Assert.Equal([VolumeKey.Mute], tracker.Update(2, [(Page, ConsumerKeyTracker.Mute)]));
    }

    [Fact]
    public void OtherUsagesAndPagesAreIgnored()
    {
        var tracker = new ConsumerKeyTracker();

        Assert.Empty(tracker.Update(1, [(Page, 0xCD)]));                                 // Play/Pause
        Assert.Empty(tracker.Update(1, [(0x07, ConsumerKeyTracker.VolumeIncrement)]));   // keyboard page
    }

    [Fact]
    public void SecondKeyWhileFirstHeldRaisesOnlyTheNewOne()
    {
        var tracker = new ConsumerKeyTracker();
        tracker.Update(1, [(Page, ConsumerKeyTracker.VolumeDecrement)]);

        Assert.Equal([VolumeKey.Mute],
            tracker.Update(1, [(Page, ConsumerKeyTracker.VolumeDecrement), (Page, ConsumerKeyTracker.Mute)]));
    }
}
