using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace SqlHistory.Hackathon.Api.Telemetry;

public sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    value => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime(),
    value => DateTime.SpecifyKind(value, DateTimeKind.Utc));

/// <summary>
/// Hackathon-only SQLite store for statistics samples, plan snapshots and trace spans.
/// Kept in its own file (sqlhistory-telemetry.db) so routine history is never affected;
/// the schema is created with EnsureCreated, so delete the file after changing it.
/// </summary>
public class TelemetryDbContext : DbContext
{
    public TelemetryDbContext(DbContextOptions<TelemetryDbContext> options) : base(options)
    {
    }

    public DbSet<StatsSampleEntity> StatsSamples => Set<StatsSampleEntity>();

    public DbSet<StatementSampleEntity> StatementSamples => Set<StatementSampleEntity>();

    public DbSet<StatementTextEntity> StatementTexts => Set<StatementTextEntity>();

    public DbSet<FunctionSampleEntity> FunctionSamples => Set<FunctionSampleEntity>();

    public DbSet<PlanSnapshotEntity> PlanSnapshots => Set<PlanSnapshotEntity>();

    public DbSet<SpanEntity> Spans => Set<SpanEntity>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Every timestamp here is UTC; SQLite would otherwise hand them back as DateTimeKind.Unspecified.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StatsSampleEntity>().HasIndex(x => x.SampledAtUtc);

        modelBuilder.Entity<StatementSampleEntity>(b =>
        {
            b.HasIndex(x => x.SampleId);
            b.HasIndex(x => new { x.QueryId, x.UserId, x.TopLevel });
        });

        modelBuilder.Entity<StatementTextEntity>().HasKey(x => x.QueryId);

        modelBuilder.Entity<FunctionSampleEntity>().HasIndex(x => x.SampleId);

        modelBuilder.Entity<PlanSnapshotEntity>().HasIndex(x => new { x.QueryKey, x.CapturedAtUtc });

        modelBuilder.Entity<SpanEntity>(b =>
        {
            b.HasKey(x => new { x.TraceId, x.SpanId });
            b.HasIndex(x => x.StartUtc);
            b.HasIndex(x => new { x.RoutineSchema, x.RoutineName });
        });
    }
}

/// <summary>One sampling run. Statement and function rows hold cumulative counters at that moment.</summary>
public sealed class StatsSampleEntity
{
    public long Id { get; set; }

    public DateTime SampledAtUtc { get; set; }

    /// <summary>pg_stat_statements_info.stats_reset; null when pg_stat_statements is unavailable.</summary>
    public DateTime? StatementsResetAtUtc { get; set; }

    /// <summary>pg_stat_statements_info.dealloc: entries evicted so far.</summary>
    public long? StatementsDealloc { get; set; }

    /// <summary>pg_stat_database.stats_reset, which also resets pg_stat_user_functions.</summary>
    public DateTime? DatabaseStatsResetAtUtc { get; set; }

    public bool StatementsAvailable { get; set; }
}

public sealed class StatementSampleEntity
{
    public long Id { get; set; }

    public long SampleId { get; set; }

    public long QueryId { get; set; }

    public long UserId { get; set; }

    public bool TopLevel { get; set; }

    public long Calls { get; set; }

    public double TotalExecTimeMs { get; set; }

    public long Rows { get; set; }
}

/// <summary>Normalized statement text (constants replaced by $n, so no parameter values).</summary>
public sealed class StatementTextEntity
{
    public long QueryId { get; set; }

    public required string Query { get; set; }
}

public sealed class FunctionSampleEntity
{
    public long Id { get; set; }

    public long SampleId { get; set; }

    public long FunctionOid { get; set; }

    public required string SchemaName { get; set; }

    public required string FunctionName { get; set; }

    public required string IdentityArguments { get; set; }

    public long Calls { get; set; }

    public double TotalTimeMs { get; set; }

    public double SelfTimeMs { get; set; }
}

public sealed class PlanSnapshotEntity
{
    public long Id { get; set; }

    public required string QueryKey { get; set; }

    public DateTime CapturedAtUtc { get; set; }

    public required string Database { get; set; }

    public required string ServerVersion { get; set; }

    /// <summary>The routine the query exercises and its latest recorded version at capture time, if any.</summary>
    public string? RoutineDisplayName { get; set; }

    public int? RoutineVersion { get; set; }

    public double TotalCost { get; set; }

    public required string PlanJson { get; set; }
}

public sealed class SpanEntity
{
    public required string TraceId { get; set; }

    public required string SpanId { get; set; }

    public string? ParentSpanId { get; set; }

    public required string Name { get; set; }

    public required string Kind { get; set; }

    public required string ServiceName { get; set; }

    public DateTime StartUtc { get; set; }

    public double DurationMs { get; set; }

    public bool IsError { get; set; }

    public string? StatusDescription { get; set; }

    public string? HttpMethod { get; set; }

    public string? HttpRoute { get; set; }

    public int? HttpStatusCode { get; set; }

    /// <summary>Application-declared caller type (sqlhistory.caller.type), if the app set one.</summary>
    public string? DeclaredCallerType { get; set; }

    public string? RoutineSchema { get; set; }

    public string? RoutineName { get; set; }

    public string? RoutineArguments { get; set; }

    public string? RoutineType { get; set; }

    /// <summary>Allow-listed attributes only, as JSON.</summary>
    public required string AttributesJson { get; set; }

    public DateTime ReceivedAtUtc { get; set; }
}
