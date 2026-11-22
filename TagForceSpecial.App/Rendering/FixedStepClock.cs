namespace TagForceSpecial.PC.Rendering;

/// <summary>Fixed-rate update steps from variable host frame times: the MonoGame host updated at a fixed 60 Hz, and every presentation
/// timing counts those steps. Long stalls run at most <see cref="MaxStepsPerAdvance"/> steps and drop the rest, so time never piles up.</summary>
public sealed class FixedStepClock(double stepsPerSecond = 60)
{
    public const int MaxStepsPerAdvance = 4;
    private readonly double step = 1 / stepsPerSecond;
    private double pending;

    /// <summary>Steps run since start.</summary>
    public long Steps { get; private set; }

    /// <summary>Adds <paramref name="seconds"/> of host time and returns how many whole steps to run now.</summary>
    public int Advance(double seconds)
    {
        if (seconds < 0 || double.IsNaN(seconds)) throw new ArgumentOutOfRangeException(nameof(seconds));
        pending += seconds;
        int due = (int)Math.Floor(pending / step + 1e-9);
        int run = Math.Min(due, MaxStepsPerAdvance);
        pending = due > MaxStepsPerAdvance ? 0 : pending - run * step;
        Steps += run;
        return run;
    }
}
