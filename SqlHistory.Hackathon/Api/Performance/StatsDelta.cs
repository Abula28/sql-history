namespace SqlHistory.Hackathon.Api.Performance;

/// <summary>Cumulative counters of one statement or function at one sample.</summary>
/// <param name="Rows">Rows processed (statements only).</param>
/// <param name="SelfTimeMs">Time excluding nested functions (functions only).</param>
public sealed record StatCounters(long Calls, double TotalTimeMs, long Rows = 0, double SelfTimeMs = 0);

/// <summary>All counters of one sample, plus what is needed to recognise resets.</summary>
/// <param name="ResetAtUtc">When the statistics were last reset; a change means a reset between samples.</param>
/// <param name="Evictions">Entries evicted so far (pg_stat_statements dealloc), if known.</param>
public sealed record StatsSnapshot<TKey>(
    DateTime SampledAtUtc,
    DateTime? ResetAtUtc,
    long? Evictions,
    IReadOnlyDictionary<TKey, StatCounters> Counters) where TKey : notnull;

public enum DeltaOrigin
{
    /// <summary>Current minus previous.</summary>
    Difference,

    /// <summary>The entry did not exist at the previous sample, so all of its counters are new.</summary>
    NewEntry,

    /// <summary>Counters restarted (global reset, or the entry was evicted and re-created).
    /// The delta is the activity since the restart; activity just before it is lost.</summary>
    AfterReset
}

public sealed record StatDelta<TKey>(TKey Key, long Calls, double TotalTimeMs, long Rows, double SelfTimeMs,
    DeltaOrigin Origin)
{
    public double MeanTimeMs => Calls == 0 ? 0 : TotalTimeMs / Calls;
}

/// <param name="StatsReset">Statistics were reset between the samples.</param>
/// <param name="PossiblyIncomplete">Entries were evicted between the samples, so their activity is missing.</param>
public sealed record StatsInterval<TKey>(
    DateTime FromUtc,
    DateTime ToUtc,
    bool StatsReset,
    bool PossiblyIncomplete,
    IReadOnlyList<StatDelta<TKey>> Deltas)
{
    public long TotalCalls => Deltas.Sum(x => x.Calls);

    public double TotalTimeMs => Deltas.Sum(x => x.TotalTimeMs);
}

/// <summary>
/// Turns two cumulative samples into per-interval activity. Never produces negative values:
/// a counter that went down means it restarted, and the current value is used instead.
/// </summary>
public static class StatsDelta
{
    public static StatsInterval<TKey> Compute<TKey>(StatsSnapshot<TKey> previous, StatsSnapshot<TKey> current)
        where TKey : notnull
    {
        var globalReset = previous.ResetAtUtc != current.ResetAtUtc;
        var possiblyIncomplete = previous.Evictions.HasValue && current.Evictions.HasValue &&
                                 current.Evictions > previous.Evictions;

        var deltas = new List<StatDelta<TKey>>();

        foreach (var (key, now) in current.Counters)
        {
            StatDelta<TKey> delta;

            if (globalReset)
            {
                delta = FromCurrent(key, now, DeltaOrigin.AfterReset);
            }
            else if (!previous.Counters.TryGetValue(key, out var before))
            {
                // Absent from a complete earlier sample, so it was created after it.
                delta = FromCurrent(key, now, DeltaOrigin.NewEntry);
            }
            else if (now.Calls < before.Calls || now.TotalTimeMs < before.TotalTimeMs ||
                     now.Rows < before.Rows || now.SelfTimeMs < before.SelfTimeMs)
            {
                delta = FromCurrent(key, now, DeltaOrigin.AfterReset);
            }
            else
            {
                delta = new StatDelta<TKey>(key,
                    now.Calls - before.Calls,
                    now.TotalTimeMs - before.TotalTimeMs,
                    now.Rows - before.Rows,
                    now.SelfTimeMs - before.SelfTimeMs,
                    DeltaOrigin.Difference);
            }

            if (delta.Calls > 0)
                deltas.Add(delta);
        }

        return new StatsInterval<TKey>(previous.SampledAtUtc, current.SampledAtUtc, globalReset,
            possiblyIncomplete, deltas);
    }

    private static StatDelta<TKey> FromCurrent<TKey>(TKey key, StatCounters now, DeltaOrigin origin)
    {
        return new StatDelta<TKey>(key, now.Calls, now.TotalTimeMs, now.Rows, now.SelfTimeMs, origin);
    }
}
