using SqlHistory.Hackathon.AiAnalysis;
using SqlHistory.PostgreSql.Routines;

namespace SqlHistory.Hackathon.Api.Contracts;

public sealed record StatusDto(DatabaseStatusDto Database, ScanStatusDto Scan, AiStatusDto Ai);

public sealed record DatabaseStatusDto(
    string Provider,
    bool Connected,
    string? Database,
    string? User,
    string? ServerVersion,
    string? Error);

public sealed record ScanStatusDto(
    DateTime? LastScanAtUtc,
    ScanSummaryDto? LastScan,
    DateTime? LastFailureAtUtc,
    string? LastError,
    int AutoScanIntervalSeconds);

/// <param name="EndpointHost">Where routine definitions are sent when analysis runs.</param>
public sealed record AiStatusDto(bool Configured, string Model, string EndpointHost);

public sealed record AnalyzeRequest(int From, int To);

public sealed record AnalysisDto(
    int FromVersion,
    int ToVersion,
    string Summary,
    IReadOnlyList<string> Changes,
    IReadOnlyList<string> Risks,
    RiskLevel RiskLevel,
    IReadOnlyList<string> Recommendations,
    bool EmbeddedInstructionsDetected,
    string Model,
    string SentTo);

public sealed record RollbackPreviewRequest(int Version);

/// <param name="SqlToExecute">Exactly the statement a restore would run.</param>
public sealed record RollbackPreviewDto(
    int TargetVersion,
    string SqlToExecute,
    string? CurrentDefinition,
    string? CurrentDefinitionHash,
    bool CanRestore,
    RestoreStatus Status,
    string Message);

/// <param name="ExpectedCurrentHash">CurrentDefinitionHash from the preview the user approved.</param>
/// <param name="Confirm">Must be true; a deliberate second step after the preview.</param>
public sealed record RollbackRequest(int Version, string ExpectedCurrentHash, bool Confirm);

public sealed record RollbackResultDto(
    RestoreStatus Status,
    string Message,
    int? RecordedVersion);
