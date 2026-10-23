namespace DazeMediaOsd.Core.Audio;

/// <summary>What the cycle-output hotkey switches to.</summary>
public static class DeviceCycle
{
    /// <summary>
    /// The choice after <paramref name="currentId"/> in <see cref="DeviceChoices.For"/> order (by name), wrapping
    /// around; the first one when the current default isn't listed. Null when there is nothing else to pick.
    /// </summary>
    public static AudioEndpoint? Next(IReadOnlyList<AudioEndpoint> endpoints, string? currentId)
    {
        var choices = DeviceChoices.For(endpoints, currentId);
        var index = choices.ToList().FindIndex(e => e.Id == currentId);
        var next = choices.Count == 0 ? null : choices[(index + 1) % choices.Count];
        return next is null || next.Id == currentId ? null : next;
    }
}
