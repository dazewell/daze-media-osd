namespace DazeMediaOsd.Core.Brightness;

/// <summary>
/// The built-in screen as it is now. <paramref name="Level"/> is the 0–100 percent it shows; <paramref name="Levels"/>
/// the percents it accepts (often all 101, sometimes a handful). <paramref name="Id"/> names it for
/// <see cref="IBrightnessBackend.Write"/>.
/// </summary>
public sealed record BrightnessPanel(string Id, string Name, int Level, IReadOnlyList<int> Levels);

/// <summary>What the OSD shows for a brightness step: the level the screen is at now (D-039).</summary>
public sealed record BrightnessState(string Name, int Percent);

/// <summary>Reads and sets the built-in screen. Not thread-safe: only <see cref="BrightnessService"/>'s worker calls it.</summary>
public interface IBrightnessBackend : IDisposable
{
    /// <summary>The built-in screen, read now; null when there is none (a desktop, a closed lid). Throws when reading fails.</summary>
    BrightnessPanel? Read();

    /// <summary>
    /// Sets the screen <paramref name="id"/> to <paramref name="level"/>, a percent the screen's
    /// <see cref="BrightnessPanel.Levels"/> lists. Throws when it fails; never retries.
    /// </summary>
    void Write(string id, int level);

    /// <summary>Drops what was connected: the next call starts over. Called after any failure.</summary>
    void Reset();
}

public interface IBrightnessService : IDisposable
{
    /// <summary>Name of the built-in screen the wheel changes; null when none was found. Only meaningful once <see cref="Probed"/>.</summary>
    string? ScreenName { get; }

    /// <summary>The service has looked for the screen at least once.</summary>
    bool Probed { get; }

    /// <summary>Raised on the service's own thread when <see cref="ScreenName"/> or <see cref="Probed"/> changed.</summary>
    event EventHandler? ScreenChanged;

    /// <summary>Looks for the screen again, without touching its brightness. Returns at once; <see cref="ScreenChanged"/> follows.</summary>
    void Refresh();

    /// <summary>
    /// Asks to move the brightness by <paramref name="percent"/> points (negative: darker). Returns at once; steps
    /// that arrive while one is being applied are added up and applied as one. A failure only logs.
    /// </summary>
    void Adjust(int percent);

    /// <summary>
    /// A step was applied or already at its limit, with the level the screen reports afterwards. Raised on the
    /// service's own thread; marshal before touching UI. Not raised when nothing could be read or written.
    /// </summary>
    event EventHandler<BrightnessState>? Changed;
}
