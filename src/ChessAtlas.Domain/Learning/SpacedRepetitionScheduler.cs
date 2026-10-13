namespace ChessAtlas.Domain.Learning;

/// <summary>
/// An SM-2 derived spaced repetition scheduler.
///
/// <para>
/// SM-2 rather than something bespoke because it is the algorithm the behaviour is judged against:
/// a trainer whose intervals do not grow roughly geometrically on success and collapse on failure
/// feels broken to anyone who has used flashcards. The deviations are deliberate and small —
/// an explicit relearn step for lapses, a "Hard" band, and a configurable interval ceiling so an
/// opening line can never vanish for years.
/// </para>
///
/// <para>
/// The scheduler is a pure function of (<see cref="ReviewSchedule"/>, <see cref="ReviewVerdict"/>,
/// now). No clock, no randomness, no state — which is what lets the test suite assert exact
/// intervals and lets the trainer preview "what would happen if I answered Easy" before committing.
/// </para>
/// </summary>
public sealed class SpacedRepetitionScheduler
{
    private readonly SchedulingSettings _settings;

    public SpacedRepetitionScheduler(SchedulingSettings? settings = null)
    {
        _settings = settings ?? SchedulingSettings.Default;
        _settings.Validate();
    }

    public SchedulingSettings Settings => _settings;

    /// <summary>The schedule for an item that has never been reviewed.</summary>
    public ReviewSchedule NewItem(DateTimeOffset now) =>
        new(
            repetitions: 0,
            easeFactor: _settings.InitialEaseFactor,
            interval: TimeSpan.Zero,
            lapses: 0,
            dueAt: now);

    public ReviewSchedule Review(ReviewSchedule current, ReviewVerdict verdict, DateTimeOffset now) =>
        verdict switch
        {
            ReviewVerdict.Again => Lapse(current, now),
            ReviewVerdict.Hard => Pass(current, now, _settings.HardIntervalMultiplier, -_settings.HardEasePenalty),
            ReviewVerdict.Good => Pass(current, now, intervalGrowthOverride: null, easeDelta: 0),
            ReviewVerdict.Easy => Pass(current, now, null, _settings.EasyEaseBonus, _settings.EasyIntervalBonus),
            _ => throw new ArgumentOutOfRangeException(nameof(verdict), verdict, "Unknown review verdict."),
        };

    /// <summary>
    /// Convenience for the common case: schedule the very first review of an item the learner has
    /// just seen, without having to construct a <see cref="ReviewSchedule"/> by hand.
    /// </summary>
    public ReviewSchedule ReviewNew(ReviewVerdict verdict, DateTimeOffset now) =>
        Review(NewItem(now), verdict, now);

    private ReviewSchedule Lapse(ReviewSchedule current, DateTimeOffset now)
    {
        var ease = ClampEase(current.EaseFactor - _settings.LapseEasePenalty);

        // A lapse on a fresh item has no interval to shrink, so it simply re-enters learning.
        var interval = current.Interval > TimeSpan.Zero
            ? ClampInterval(TimeSpan.FromTicks((long)(current.Interval.Ticks * _settings.LapseIntervalMultiplier)))
            : _settings.RelearnInterval;

        if (interval < _settings.RelearnInterval) interval = _settings.RelearnInterval;

        return new ReviewSchedule(
            repetitions: 0,
            easeFactor: ease,
            interval: interval,
            lapses: current.Lapses + 1,
            dueAt: now + interval);
    }

    private ReviewSchedule Pass(
        ReviewSchedule current,
        DateTimeOffset now,
        double? intervalGrowthOverride,
        double easeDelta,
        double intervalBonus = 1.0)
    {
        var ease = ClampEase(current.EaseFactor + easeDelta);

        TimeSpan interval;
        if (current.Repetitions <= 0)
        {
            interval = _settings.FirstInterval;
        }
        else if (current.Repetitions == 1)
        {
            interval = _settings.SecondInterval;
        }
        else
        {
            var growth = intervalGrowthOverride ?? current.EaseFactor;
            interval = TimeSpan.FromTicks((long)(current.Interval.Ticks * growth * intervalBonus));
        }

        interval = ClampInterval(interval);

        return new ReviewSchedule(
            repetitions: current.Repetitions + 1,
            easeFactor: ease,
            interval: interval,
            lapses: current.Lapses,
            dueAt: now + interval);
    }

    private double ClampEase(double ease) =>
        Math.Clamp(ease, _settings.MinimumEaseFactor, _settings.MaximumEaseFactor);

    private TimeSpan ClampInterval(TimeSpan interval)
    {
        if (interval <= TimeSpan.Zero) interval = TimeSpan.FromMinutes(1);
        return interval > _settings.MaximumInterval ? _settings.MaximumInterval : interval;
    }

    /// <summary>
    /// Projects the schedule forward without recording anything, so the trainer can label each
    /// button with the interval it would produce ("Good — 3d").
    /// </summary>
    public IReadOnlyDictionary<ReviewVerdict, TimeSpan> Preview(ReviewSchedule current, DateTimeOffset now)
    {
        var preview = new Dictionary<ReviewVerdict, TimeSpan>();
        foreach (var verdict in new[]
                 {
                     ReviewVerdict.Again, ReviewVerdict.Hard, ReviewVerdict.Good, ReviewVerdict.Easy,
                 })
        {
            preview[verdict] = Review(current, verdict, now).Interval;
        }

        return preview;
    }
}
