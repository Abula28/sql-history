using SqlHistory.Core.Models;

namespace SqlHistory.Hackathon.Api.Contracts;

public enum RoutineState
{
    /// <summary>In the database, never scanned.</summary>
    Untracked,

    /// <summary>In the database and identical to the latest version.</summary>
    Tracked,

    /// <summary>In the database, but differs from the latest version; the next scan records it.</summary>
    ChangedSinceLastScan,

    /// <summary>Only in history; no longer in the database.</summary>
    Dropped
}

public sealed record RoutineSummaryDto(
    string Id,
    string Schema,
    string Name,
    RoutineType RoutineType,
    string IdentityArguments,
    string DisplayName,
    RoutineState State,
    int? LatestVersion,
    DateTime? LastChangeDetectedAtUtc);

public sealed record RoutineDetailDto(
    RoutineSummaryDto Routine,
    string? CurrentDefinition,
    string? CurrentDefinitionHash);

public sealed record VersionSummaryDto(
    int VersionNumber,
    DateTime CapturedAtUtc,
    string DefinitionHash,
    int? IdenticalToVersion);

public sealed record VersionDto(
    int VersionNumber,
    DateTime CapturedAtUtc,
    string DefinitionHash,
    string? Definition);

public sealed record CompareDto(VersionDto From, VersionDto To);

public sealed record ScanChangeDto(string Id, string DisplayName, string Status, int VersionNumber);

public sealed record ScanSummaryDto(
    DateTime ScannedAtUtc,
    int NewCount,
    int ModifiedCount,
    int UnchangedCount,
    IReadOnlyList<ScanChangeDto> Changes);
