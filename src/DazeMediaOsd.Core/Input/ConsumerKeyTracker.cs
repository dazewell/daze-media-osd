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

    private readonly Dictionary<nint, HashSet<VolumeKey>> _held = [];

    public IReadOnlyList<VolumeKey> Update(nint device, IEnumerable<(ushort Page, ushort Usage)> usages)
    {
        var now = new HashSet<VolumeKey>();
        foreach (var (page, usage) in usages)
        {
            if (page == ConsumerPage && ToKey(usage) is { } key)
                now.Add(key);
        }
        var before = _held.GetValueOrDefault(device) ?? [];
        _held[device] = now;
        return [.. now.Where(key => !before.Contains(key))];
    }

    private static VolumeKey? ToKey(ushort usage) => usage switch
    {
        VolumeIncrement => VolumeKey.Up,
        VolumeDecrement => VolumeKey.Down,
        Mute => VolumeKey.Mute,
        _ => null,
    };
}
