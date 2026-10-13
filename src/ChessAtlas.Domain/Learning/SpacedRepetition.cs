namespace ChessAtlas.Domain.Learning;

/// <summary>How the learner rated their own recall. Four buckets, matching the buttons shown.</summary>
public enum ReviewVerdict
{
    /// <summary>Could not recall, or recalled wrongly.</summary>
    Again = 0,

    /// <summary>Recalled, but only after visible effort.</summary>
    Hard = 1,

    /// <summary>Recalled comfortably.</summary>
    Good = 2,

    /// <summary>Recalled instantly and with confidence.</summary>
    Easy = 3,
}

/// <summary>
/// The state a scheduled item carries between reviews. A value object: a review produces a new
/// schedule rather than mutating the old one, which is what makes the scheduler trivially testable
/// and lets the trainer replay a session without side effects.
/// </summary>
public readonly record struct ReviewSchedule
{
    public ReviewSchedule(int repetitions, double easeFactor, TimeSpan interval, int lapses, DateTimeOffset dueAt)
    {
        Repetitions = repetitions;
        EaseFactor = easeFactor;
        Interval = interval;
        Lapses = lapses;
        DueAt = dueAt;
    }

    /// <summary>Consecutive successful reviews. Reset to zero by a lapse.</summary>
    public int Repetitions { get; }

    /// <summary>Multiplier applied to the current interval on the next successful review.</summary>
    public double EaseFactor { get; }

    public TimeSpan Interval { get; }

    /// <summary>How many times the item has been forgotten after being learned.</summary>
    public int Lapses { get; }

    public DateTimeOffset DueAt { get; }

    public bool IsDue(DateTimeOffset now) => DueAt <= now;

    public bool IsNew => Repetitions == 0 && Lapses == 0;
}

/// <summary>
/// Tuning knobs for <see cref="SpacedRepetitionScheduler"/>. Every value the product exposes in
/// Settings lives here, so changing the feel of the trainer is a data change, not a code change.
/// </summary>
public sealed record SchedulingSettings
{
    /// <summary>Interval after the first successful review.</summary>
    public TimeSpan FirstInterval { get; init; } = TimeSpan.FromDays(1);

    /// <summary>Interval after the second successful review.</summary>
    public TimeSpan SecondInterval { get; init; } = TimeSpan.FromDays(3);

    /// <summary>Where to put an item that was just forgotten — usually "again in this session".</summary>
    public TimeSpan RelearnInterval { get; init; } = TimeSpan.FromMinutes(10);

    public double InitialEaseFactor { get; init; } = 2.5;
    public double MinimumEaseFactor { get; init; } = 1.3;
    public double MaximumEaseFactor { get; init; } = 3.5;

    /// <summary>Applied to the previous interval after a lapse.</summary>
    public double LapseIntervalMultiplier { get; init; } = 0.5;

    /// <summary>How much a lapse lowers the ease factor.</summary>
    public double LapseEasePenalty { get; init; } = 0.20;

    /// <summary>How much "Hard" lowers the ease factor.</summary>
    public double HardEasePenalty { get; init; } = 0.15;

    /// <summary>How much "Easy" raises the ease factor.</summary>
    public double EasyEaseBonus { get; init; } = 0.15;

    /// <summary>Extra growth granted for an "Easy" answer.</summary>
    public double EasyIntervalBonus { get; init; } = 1.3;

    /// <summary>Growth for a "Hard" answer that still counts as a pass.</summary>
    public double HardIntervalMultiplier { get; init; } = 1.2;

    /// <summary>Ceiling on any interval, so a line never disappears for years.</summary>
    public TimeSpan MaximumInterval { get; init; } = TimeSpan.FromDays(365);

    /// <summary>
    /// Validates the settings and throws if any combination would produce a nonsense schedule
    /// (negative intervals, an ease floor above the ceiling, a lapse that grows the interval).
    /// </summary>
    public void Validate()
    {
        if (FirstInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(FirstInterval), "The first interval must be positive.");
        if (SecondInterval < FirstInterval)
            throw new ArgumentOutOfRangeException(nameof(SecondInterval), "The second interval cannot be shorter than the first.");
        if (RelearnInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(RelearnInterval), "The relearn interval must be positive.");
        if (MaximumInterval <= SecondInterval)
            throw new ArgumentOutOfRangeException(nameof(MaximumInterval), "The maximum interval must exceed the second interval.");
        if (MinimumEaseFactor <= 1.0)
            throw new ArgumentOutOfRangeException(nameof(MinimumEaseFactor), "The ease floor must be above 1.0 or intervals would shrink forever.");
        if (MaximumEaseFactor <= MinimumEaseFactor)
            throw new ArgumentOutOfRangeException(nameof(MaximumEaseFactor), "The ease ceiling must exceed the ease floor.");
        if (LapseIntervalMultiplier is <= 0 or >= 1)
            throw new ArgumentOutOfRangeException(nameof(LapseIntervalMultiplier), "A lapse must shrink the interval, so the multiplier belongs in (0, 1).");
        if (LapseEasePenalty < 0 || HardEasePenalty < 0 || EasyEaseBonus < 0)
            throw new ArgumentOutOfRangeException(nameof(LapseEasePenalty), "Ease adjustments are magnitudes and must not be negative.");
        if (HardIntervalMultiplier < 1.0)
            throw new ArgumentOutOfRangeException(nameof(HardIntervalMultiplier), "A hard but correct answer must not shrink the interval.");
        if (EasyIntervalBonus < 1.0)
            throw new ArgumentOutOfRangeException(nameof(EasyIntervalBonus), "An easy answer must not grow slower than a good one.");
    }

    public static SchedulingSettings Default => new();
}
