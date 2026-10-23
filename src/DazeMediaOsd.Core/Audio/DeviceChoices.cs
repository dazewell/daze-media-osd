using DazeMediaOsd.Core.Osd;

namespace DazeMediaOsd.Core.Audio;

/// <summary>Which endpoints quick switching offers (tray menu, hotkey cycle), and in what order.</summary>
public static class DeviceChoices
{
    /// <summary>
    /// <paramref name="endpoints"/> sorted by name, without the Bluetooth hands-free output of a product that
    /// also has a stereo one: "Headset (Technics EAH-AZ80 Hands-Free)" next to "Headphones (Technics
    /// EAH-AZ80)" is the same earbuds in call quality, never what a user switching outputs wants.
    /// <paramref name="currentId"/> stays even then, so the menu can still show what is checked.
    /// </summary>
    public static IReadOnlyList<AudioEndpoint> For(IReadOnlyList<AudioEndpoint> endpoints, string? currentId)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var stereo = endpoints
            .Where(e => e.Kind == DeviceKind.Headphones)
            .Select(e => Product(e.Name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return endpoints
            .Where(e => e.Id == currentId
                || !(e.Bluetooth && e.Kind == DeviceKind.Headset && stereo.Contains(Product(e.Name))))
            .OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(e => e.Id, StringComparer.Ordinal)
            .ToList();
    }

    // The hands-free endpoint's product carries a suffix the stereo one lacks (26300, 2026-10-23).
    private static string Product(string name)
    {
        var product = DeviceNotice.Product(name);
        return product.EndsWith(" Hands-Free", StringComparison.OrdinalIgnoreCase) ? product[..^" Hands-Free".Length] : product;
    }
}