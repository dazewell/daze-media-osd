namespace DazeMediaOsd.Core.Input;

public enum VolumeKey
{
    Up,
    Down,
    Mute,
}

public sealed class VolumeKeyEventArgs(VolumeKey key) : EventArgs
{
    public VolumeKey Key { get; } = key;
}

public interface IKeySource : IDisposable
{
    /// <summary>Raised on key down (auto-repeat included) on the source's own thread.</summary>
    event EventHandler<VolumeKeyEventArgs>? KeyPressed;
}
