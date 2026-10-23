namespace DazeMediaOsd.Core.Input;

/// <summary>
/// Turns Consumer Control HID reports into key-down events. A report lists the usages held right now
/// (an empty one is the release), so a key is "pressed" when it appears in a device's report and was
/// absent from that device's previous one.
/// </summary>
internal sealed class ConsumerKeyTracker
{
    internal const ushort ConsumerPage = 0x0C;
    internal const ushort VolumeIncrement = 0xE9;
    internal const ushort VolumeDecrement = 0xEA;
    internal const ushort Mute = 0xE2;
    internal const ushort PlayPause = 0xCD;
    internal const ushort Play = 0xB0;
    internal const ushort Pause = 0xB1;
    internal const ushort ScanNextTrack = 0xB5;
    internal const ushort ScanPreviousTrack = 0xB6;

    private readonly Dictionary<nint, HashSet<MediaKey>> _held = [];

    public IReadOnlyList<MediaKey> Update(nint device, IEnumerable<(ushort Page, ushort Usage)> usages)
    {
        var now = new HashSet<MediaKey>();
        foreach (var (page, usage) in usages)
        {
            if (page == ConsumerPage && ToKey(usage) is { } key)
                now.Add(key);
        }
        var before = _held.GetValueOrDefault(device) ?? [];
        _held[device] = now;
        return [.. now.Where(key => !before.Contains(key))];
    }

    private static MediaKey? ToKey(ushort usage) => usage switch
    {
        VolumeIncrement => MediaKey.VolumeUp,
        VolumeDecrement => MediaKey.VolumeDown,
        Mute => MediaKey.Mute,
        // Separate Play and Pause buttons exist on some remotes; the OSD treats them alike.
        PlayPause or Play or Pause => MediaKey.PlayPause,
        ScanNextTrack => MediaKey.NextTrack,
        ScanPreviousTrack => MediaKey.PreviousTrack,
        _ => null,
    };
}
