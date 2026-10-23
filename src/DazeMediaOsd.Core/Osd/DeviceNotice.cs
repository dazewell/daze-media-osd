using DazeMediaOsd.Core.Audio;

namespace DazeMediaOsd.Core.Osd;

/// <summary>
/// What one device connect/disconnect burst shows in the OSD's event line (D-014): the device, as one item
/// even when it is several endpoints, and whether it came or went. <paramref name="Others"/> counts further
/// devices in the same burst. <paramref name="Name"/> is "" when Windows no longer gave one.
/// </summary>
public sealed record DeviceNotice(string Name, EndpointFlow Flow, bool Connected, bool Bluetooth, int Others = 0)
{
    /// <summary>
    /// One notice for a burst, null for none. Endpoints of one product ("Headphones (WH-1000XM5)" +
    /// "Headset (WH-1000XM5)") merge into "WH-1000XM5"; the latest product of the burst is the headline.
    /// </summary>
    public static DeviceNotice? Summarize(IReadOnlyList<DeviceEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (events.Count == 0)
            return null;
        var groups = events
            .Select((e, i) => (Event: e, Index: i))
            .GroupBy(x => Product(x.Event.Name), StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Max(x => x.Index))
            .ToList();
        var headline = groups[^1].Select(x => x.Event).ToList();
        var last = headline[^1];
        var bluetooth = headline.Any(e => e.Bluetooth);
        // A Bluetooth device and a multi-endpoint USB headset read better as the product; a single endpoint
        // keeps its full name: "Stereo Mix (Realtek(R) Audio)" must not become "Realtek(R) Audio".
        var distinct = headline.Select(e => e.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var name = bluetooth || distinct > 1 ? groups[^1].Key : StripIndex(last.Name);
        // Headphone rather than mic when the device has both.
        var flow = headline.Any(e => e.Flow == EndpointFlow.Output) ? EndpointFlow.Output : EndpointFlow.Input;
        return new DeviceNotice(name, flow, last.Connected, bluetooth, groups.Count - 1);
    }

    /// <summary>"Headphones (WH-1000XM5)" → "WH-1000XM5"; the whole name when it has no trailing "(…)".</summary>
    internal static string Product(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        name = name.Trim();
        if (!name.EndsWith(')'))
            return StripIndex(name);
        // Match the trailing group's own "(": the product may contain parentheses itself, "Realtek(R) Audio".
        var depth = 0;
        for (var i = name.Length - 1; i >= 0; i--)
        {
            depth += name[i] switch { ')' => 1, '(' => -1, _ => 0 };
            if (depth == 0)
            {
                var inner = name[(i + 1)..^1].Trim();
                return inner.Length == 0 ? StripIndex(name) : StripIndex(inner);
            }
        }
        return StripIndex(name);
    }

    // Windows numbers a second instance of a name: "2- WH-1000XM5".
    private static string StripIndex(string name)
    {
        var dash = name.IndexOf("- ", StringComparison.Ordinal);
        return dash > 0 && name[..dash].All(char.IsAsciiDigit) ? name[(dash + 2)..].Trim() : name;
    }
}
