namespace DazeMediaOsd.Core.Brightness;

/// <summary>Decides where a wheel step takes the brightness; pure, so every edge is a unit test (D-041).</summary>
public static class BrightnessPlanner
{
    /// <summary>
    /// The wheel never goes below this percent: a screen at 0 % can be unreadable, with nothing on it to say why
    /// and the wheel the only way back.
    /// </summary>
    public const int Floor = 5;

    /// <summary>
    /// The level to set after moving <paramref name="delta"/> points from <paramref name="current"/>, or
    /// <paramref name="current"/> itself when there is nowhere to go. Always one of <paramref name="levels"/>, never
    /// below <paramref name="floor"/>, always strictly in the direction of the step: a step down never raises the
    /// brightness, not even from a level below the floor that another program set. On a screen with few levels
    /// every step still moves one level. Nothing outside 0–100 is ever chosen.
    /// </summary>
    public static int Plan(int current, IReadOnlyList<int> levels, int delta, int floor = Floor)
    {
        ArgumentNullException.ThrowIfNull(levels);
        if (delta == 0)
            return current;
        var up = delta > 0;
        var wanted = (long)current + delta;
        var best = current;
        var found = false;
        foreach (var level in levels)
        {
            if (level < floor || level is < 0 or > 100 || (up ? level <= current : level >= current))
                continue;
            if (!found || Nearer(level, best, wanted, current))
            {
                best = level;
                found = true;
            }
        }
        return best;
    }

    // Nearest to where the step points; between two equally near, the smaller move.
    private static bool Nearer(int a, int b, long wanted, int current)
    {
        var (da, db) = (Math.Abs(a - wanted), Math.Abs(b - wanted));
        return da != db ? da < db : Math.Abs(a - current) < Math.Abs(b - current);
    }
}
