using ChessAtlas.Domain.Learning;
using Xunit;

namespace ChessAtlas.Domain.Tests.Learning;

public sealed class SpacedRepetitionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    private static SpacedRepetitionScheduler Scheduler(SchedulingSettings? settings = null) => new(settings);

    [Fact]
    public void A_new_item_is_due_immediately()
    {
        var schedule = Scheduler().NewItem(Now);

        Assert.True(schedule.IsNew);
        Assert.True(schedule.IsDue(Now));
        Assert.Equal(TimeSpan.Zero, schedule.Interval);
        Assert.Equal(0, schedule.Repetitions);
        Assert.Equal(0, schedule.Lapses);
    }

    [Fact]
    public void Successful_reviews_grow_the_interval_geometrically()
    {
        var scheduler = Scheduler();
        var schedule = scheduler.NewItem(Now);

        schedule = scheduler.Review(schedule, ReviewVerdict.Good, Now);
        Assert.Equal(TimeSpan.FromDays(1), schedule.Interval);
        Assert.Equal(Now.AddDays(1), schedule.DueAt);

        schedule = scheduler.Review(schedule, ReviewVerdict.Good, Now.AddDays(1));
        Assert.Equal(TimeSpan.FromDays(3), schedule.Interval);

        var third = scheduler.Review(schedule, ReviewVerdict.Good, Now.AddDays(4));
        // 3 days * 2.5 ease = 7.5 days
        Assert.Equal(TimeSpan.FromDays(7.5), third.Interval);
        Assert.Equal(3, third.Repetitions);
    }

    [Fact]
    public void An_easy_answer_grows_faster_and_raises_the_ease_factor()
    {
        var scheduler = Scheduler();
        var afterFirst = scheduler.ReviewNew(ReviewVerdict.Good, Now);
        var afterSecond = scheduler.Review(afterFirst, ReviewVerdict.Good, Now.AddDays(1));

        var easy = scheduler.Review(afterSecond, ReviewVerdict.Easy, Now.AddDays(4));
        var good = scheduler.Review(afterSecond, ReviewVerdict.Good, Now.AddDays(4));

        Assert.True(easy.Interval > good.Interval);
        Assert.True(easy.EaseFactor > good.EaseFactor);
    }

    [Fact]
    public void A_lapse_resets_progress_and_shrinks_the_interval()
    {
        var scheduler = Scheduler();
        var schedule = scheduler.NewItem(Now);
        schedule = scheduler.Review(schedule, ReviewVerdict.Good, Now);
        schedule = scheduler.Review(schedule, ReviewVerdict.Good, Now.AddDays(1));
        Assert.Equal(TimeSpan.FromDays(3), schedule.Interval);

        var lapsed = scheduler.Review(schedule, ReviewVerdict.Again, Now.AddDays(4));

        Assert.Equal(0, lapsed.Repetitions);
        Assert.Equal(1, lapsed.Lapses);
        Assert.Equal(TimeSpan.FromDays(1.5), lapsed.Interval);
        Assert.True(lapsed.EaseFactor < schedule.EaseFactor);
    }

    [Fact]
    public void Repeated_lapses_cannot_push_the_ease_factor_below_the_floor()
    {
        var scheduler = Scheduler();
        var schedule = scheduler.NewItem(Now);

        for (var i = 0; i < 40; i++) schedule = scheduler.Review(schedule, ReviewVerdict.Again, Now.AddDays(i));

        Assert.Equal(scheduler.Settings.MinimumEaseFactor, schedule.EaseFactor);
        Assert.Equal(40, schedule.Lapses);
    }

    [Fact]
    public void A_lapse_on_a_fresh_item_goes_to_the_relearn_step()
    {
        var scheduler = Scheduler();

        var lapsed = scheduler.ReviewNew(ReviewVerdict.Again, Now);

        Assert.Equal(TimeSpan.FromMinutes(10), lapsed.Interval);
        Assert.Equal(1, lapsed.Lapses);
    }

    [Fact]
    public void A_hard_answer_still_passes_but_grows_slowly()
    {
        var scheduler = Scheduler();
        var schedule = scheduler.NewItem(Now);
        schedule = scheduler.Review(schedule, ReviewVerdict.Good, Now);
        schedule = scheduler.Review(schedule, ReviewVerdict.Good, Now.AddDays(1));

        var hard = scheduler.Review(schedule, ReviewVerdict.Hard, Now.AddDays(4));
        var good = scheduler.Review(schedule, ReviewVerdict.Good, Now.AddDays(4));

        // 3 days * 1.2 for hard versus 3 days * 2.5 for good. The multiplier is not exactly
        // representable in binary, so compare with a tolerance instead of an exact tick count.
        Assert.True(
            Math.Abs((hard.Interval - TimeSpan.FromDays(3.6)).Ticks) < 1000,
            $"expected ~3.6 days, got {hard.Interval}");
        Assert.True(hard.Interval < good.Interval);
        Assert.Equal(hard.Repetitions, good.Repetitions);
    }

    [Fact]
    public void Intervals_are_capped()
    {
        var settings = SchedulingSettings.Default with { MaximumInterval = TimeSpan.FromDays(30) };
        settings.Validate();

        var scheduler = Scheduler(settings);
        var schedule = scheduler.NewItem(Now);

        for (var i = 0; i < 20; i++)
            schedule = scheduler.Review(schedule, ReviewVerdict.Easy, Now.AddDays(i));

        Assert.Equal(TimeSpan.FromDays(30), schedule.Interval);
    }

    [Fact]
    public void The_scheduler_is_pure_so_the_same_input_always_gives_the_same_output()
    {
        var scheduler = Scheduler();
        var schedule = scheduler.ReviewNew(ReviewVerdict.Good, Now);
        var before = schedule;

        var preview = scheduler.Preview(schedule, Now.AddDays(1));
        var first = scheduler.Review(schedule, ReviewVerdict.Good, Now.AddDays(1));
        var second = scheduler.Review(schedule, ReviewVerdict.Good, Now.AddDays(1));

        Assert.Equal(4, preview.Count);
        Assert.Equal(first, second);
        Assert.Equal(first.Interval, preview[ReviewVerdict.Good]);
        Assert.Equal(before, schedule); // the input schedule was not mutated by any of the calls
    }

    [Fact]
    public void Preview_labels_every_button()
    {
        var scheduler = Scheduler();
        var schedule = scheduler.NewItem(Now);
        var preview = scheduler.Preview(schedule, Now);

        Assert.Equal(TimeSpan.FromMinutes(10), preview[ReviewVerdict.Again]);
        Assert.Equal(TimeSpan.FromDays(1), preview[ReviewVerdict.Good]);
        Assert.True(preview[ReviewVerdict.Easy] == preview[ReviewVerdict.Good],
            "on a brand-new item every pass starts at the first interval; only the ease factor differs");
    }

    [Fact]
    public void Unknown_verdicts_are_rejected()
    {
        var scheduler = Scheduler();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => scheduler.Review(scheduler.NewItem(Now), (ReviewVerdict)99, Now));
    }

    [Fact]
    public void Nonsense_settings_are_rejected_before_they_can_corrupt_a_schedule()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SpacedRepetitionScheduler(SchedulingSettings.Default with { FirstInterval = TimeSpan.Zero }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SpacedRepetitionScheduler(SchedulingSettings.Default with { MinimumEaseFactor = 0.9 }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SpacedRepetitionScheduler(SchedulingSettings.Default with { LapseIntervalMultiplier = 1.5 }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SpacedRepetitionScheduler(SchedulingSettings.Default with { SecondInterval = TimeSpan.FromHours(1) }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SpacedRepetitionScheduler(SchedulingSettings.Default with { HardIntervalMultiplier = 0.5 }));
    }

    [Fact]
    public void The_default_settings_are_valid() =>
        SchedulingSettings.Default.Validate();

    [Fact]
    public void A_forgotten_line_comes_back_sooner_than_one_never_seen()
    {
        // The point of tracking lapses separately: a line that was learned and then lost is not the
        // same as a line that was never attempted, and the UI should be able to say so.
        var scheduler = Scheduler();
        var fresh = scheduler.NewItem(Now);

        var learned = scheduler.ReviewNew(ReviewVerdict.Good, Now);
        learned = scheduler.Review(learned, ReviewVerdict.Good, Now.AddDays(1));
        var forgotten = scheduler.Review(learned, ReviewVerdict.Again, Now.AddDays(4));

        Assert.True(fresh.IsNew);
        Assert.False(forgotten.IsNew);
        Assert.Equal(1, forgotten.Lapses);
    }
}
