using SqlHistory.Hackathon.Api.Performance;

namespace SqlHistory.Hackathon.Tests;

public class StatsDeltaTests
{
    private static readonly DateTime T0 = new(2026, 10, 17, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Reset = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Compute_ShouldSubtractConsecutiveSamples()
    {
        var previous = Snapshot(T0, Reset, 0, ("q1", new StatCounters(10, 100, 50)));
        var current = Snapshot(T0.AddMinutes(1), Reset, 0, ("q1", new StatCounters(15, 160, 75)));

        var delta = Assert.Single(StatsDelta.Compute(previous, current).Deltas);

        Assert.Equal((5, 60d, 25L), (delta.Calls, delta.TotalTimeMs, delta.Rows));
        Assert.Equal(12, delta.MeanTimeMs);
        Assert.Equal(DeltaOrigin.Difference, delta.Origin);
    }

    [Fact]
    public void Compute_ShouldOmitStatementsWithoutNewCalls()
    {
        var previous = Snapshot(T0, Reset, 0, ("q1", new StatCounters(10, 100)));
        var current = Snapshot(T0.AddMinutes(1), Reset, 0, ("q1", new StatCounters(10, 100)));

        Assert.Empty(StatsDelta.Compute(previous, current).Deltas);
    }

    [Fact]
    public void Compute_ShouldUseCurrentValues_AfterGlobalReset()
    {
        var previous = Snapshot(T0, Reset, 0, ("q1", new StatCounters(1000, 5000)));
        var current = Snapshot(T0.AddMinutes(1), T0.AddSeconds(30), 0, ("q1", new StatCounters(3, 9)));

        var interval = StatsDelta.Compute(previous, current);

        var delta = Assert.Single(interval.Deltas);
        Assert.True(interval.StatsReset);
        Assert.Equal((3L, 9d, DeltaOrigin.AfterReset), (delta.Calls, delta.TotalTimeMs, delta.Origin));
    }

    [Fact]
    public void Compute_ShouldNeverGoNegative_WhenAnEntryWasEvictedAndRecreated()
    {
        // No global reset, but the entry's counters went down: it restarted from zero.
        var previous = Snapshot(T0, Reset, 4, ("q1", new StatCounters(500, 900)));
        var current = Snapshot(T0.AddMinutes(1), Reset, 5, ("q1", new StatCounters(2, 4)));

        var interval = StatsDelta.Compute(previous, current);

        var delta = Assert.Single(interval.Deltas);
        Assert.Equal((2L, 4d, DeltaOrigin.AfterReset), (delta.Calls, delta.TotalTimeMs, delta.Origin));
        Assert.True(interval.PossiblyIncomplete);
        Assert.False(interval.StatsReset);
    }

    [Fact]
    public void Compute_ShouldTreatTimeGoingDownAsRestart_EvenIfCallsWentUp()
    {
        var previous = Snapshot(T0, Reset, 0, ("q1", new StatCounters(5, 900)));
        var current = Snapshot(T0.AddMinutes(1), Reset, 0, ("q1", new StatCounters(8, 30)));

        var delta = Assert.Single(StatsDelta.Compute(previous, current).Deltas);

        Assert.Equal((8L, 30d, DeltaOrigin.AfterReset), (delta.Calls, delta.TotalTimeMs, delta.Origin));
    }

    [Fact]
    public void Compute_ShouldCountNewEntriesFromZero_AndIgnoreDisappearedOnes()
    {
        // q2 is new (e.g. the queryid changed after a routine was recreated); q1 disappeared.
        var previous = Snapshot(T0, Reset, 0, ("q1", new StatCounters(10, 10)));
        var current = Snapshot(T0.AddMinutes(1), Reset, 0, ("q2", new StatCounters(4, 8)));

        var delta = Assert.Single(StatsDelta.Compute(previous, current).Deltas);

        Assert.Equal(("q2", 4L, DeltaOrigin.NewEntry), (delta.Key, delta.Calls, delta.Origin));
    }

    [Fact]
    public void Compute_ShouldTreatSelfTimeDecreaseAsRestart_ForFunctions()
    {
        var previous = Snapshot(T0, Reset, null, ("f", new StatCounters(10, 100, SelfTimeMs: 80)));
        var current = Snapshot(T0.AddMinutes(1), Reset, null, ("f", new StatCounters(11, 101, SelfTimeMs: 5)));

        var delta = Assert.Single(StatsDelta.Compute(previous, current).Deltas);

        Assert.Equal(DeltaOrigin.AfterReset, delta.Origin);
        Assert.Equal(5, delta.SelfTimeMs);
    }

    [Fact]
    public void Interval_ShouldSumDeltas()
    {
        var previous = Snapshot(T0, Reset, 0, ("a", new StatCounters(1, 1)), ("b", new StatCounters(1, 1)));
        var current = Snapshot(T0.AddMinutes(1), Reset, 0, ("a", new StatCounters(3, 5)), ("b", new StatCounters(2, 3)));

        var interval = StatsDelta.Compute(previous, current);

        Assert.Equal((3L, 6d), (interval.TotalCalls, interval.TotalTimeMs));
        Assert.All(interval.Deltas, x => Assert.True(x.Calls >= 0 && x.TotalTimeMs >= 0));
    }

    private static StatsSnapshot<string> Snapshot(DateTime at, DateTime? reset, long? evictions,
        params (string Key, StatCounters Counters)[] rows)
    {
        return new StatsSnapshot<string>(at, reset, evictions, rows.ToDictionary(x => x.Key, x => x.Counters));
    }
}
