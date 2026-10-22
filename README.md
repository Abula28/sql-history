# SqlHistory

SqlHistory is an open-source tool for tracking historical versions of SQL Server stored procedures.

## Status

Early development.

Currently supported:

- SQL Server connection
- Stored procedure discovery
- Stored procedure definition retrieval
- Deterministic snapshotting and change detection (new / modified / unchanged)
- Persisted version history via EF Core + SQLite, surviving application restarts

## Planned MVP

- Version diffing
- Richer CLI commands for querying history

## Requirements

- .NET 10
- Microsoft SQL Server

## Configuration

The CLI reads configuration from `appsettings.json`, optional `appsettings.{DOTNET_ENVIRONMENT}.json`, user secrets, and environment variables (in that order).

- `ConnectionStrings:SqlHistoryStorage` — SQLite connection string for the local history database, e.g. `Data Source=sqlhistory.db`. No secrets are required for this one.
- `ServerConnectionString` — SQL Server connection string for the database being tracked. This is not committed to `appsettings.json`; set it via `dotnet user-secrets` or an environment variable, since it may contain credentials.

On startup, the CLI applies any pending EF Core migrations to the SQLite database automatically before reading stored procedures.

## Storage notes

- History is stored in a single local SQLite file (`sqlhistory.db`, plus its `-shm`/`-wal` companions while open). These are gitignored and should not be committed.
- The current implementation supports exactly one SQLite history database per CLI instance — there is no support yet for tracking multiple SQL Server databases into separate history stores. Each distinct SQL Server database you want to track needs its own `SqlHistoryStorage` connection string / working directory for now.