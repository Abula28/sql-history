# SQL Time Machine

Version control for the code that lives inside your database.

SQL Time Machine tracks every PostgreSQL function and procedure, records a new version whenever a
definition changes, shows side-by-side diffs, asks an LLM to explain what a change does to behaviour,
and restores an earlier version through a validated, confirmed, transactional workflow.

Built for the hackathon (Track 01, "Make it Better") on top of [SqlHistory](../README.md), an
open-source SQL Server stored-procedure history tool.

## The problem

Application code has Git, code review and `git revert`. The routines inside the database often have
none of that: someone runs `CREATE OR REPLACE FUNCTION` in a console, the old definition is gone, and
nobody can say what changed, when, or whether it was intentional. A one-line edit such as removing a
`WHERE is_active = true` silently changes every number built on top of it.

## Features

| Feature | What it does |
|---|---|
| Routine explorer | Lists functions and procedures by schema with type, argument signature, latest version and state: *not scanned*, *tracked*, *changed since last scan*, *dropped*. |
| Version history | Each scan stores a new version only when the normalized definition's SHA-256 changes. Reverting to earlier content is recorded as a new version and marked "same content as vN". |
| Visual diff | Monaco side-by-side, read-only diff with PostgreSQL highlighting. Defaults to previous → latest; any two versions can be compared. |
| AI change review | Summary, changes, risks, risk level and recommendations from an OpenAI-compatible model. Flags definitions that try to instruct the AI. |
| Safe rollback | Dry-run preview showing the exact SQL, explicit confirmation, execution in a transaction with verification, and the restore recorded as a new version. |
| Swagger | OpenAPI document and Swagger UI for the API (Development only). |

Changes are detected **by scanning** (the *Scan now* button, or optional polling). Nothing is detected
instantly.

## Architecture

```text
 Browser (React + Monaco)  ──/api──►  SqlHistory.Hackathon.Api  (ASP.NET Core, localhost only)
   SqlHistory.Hackathon/Web             │
                                        ├─ SqlHistory.Core            models, change detection, SnapshotScanService
                                        ├─ SqlHistory.PostgreSql      routine reader + restorer (Npgsql)  ──►  PostgreSQL (source)
                                        ├─ SqlHistory.Storage.Sqlite  EF Core history repository          ──►  SQLite (history)
                                        └─ SqlHistory.Hackathon.AiAnalysis  OpenAI SDK, prompt, parser    ──►  LLM provider
```

| Project | Role |
|---|---|
| `src/SqlHistory.Core` | Provider-independent models and logic. `StoredProcedureIdentity` = provider + schema + name + routine type + argument types, so overloads and providers never share history. |
| `src/SqlHistory.PostgreSql` | Reads routines from `pg_proc` (`pg_get_functiondef`, `oidvectortypes(proargtypes)`) and implements restore. |
| `src/SqlHistory.Storage.Sqlite` | Existing SQLite history, extended with migration `AddRoutineIdentity` (existing rows become SQL Server procedures). |
| `src/SqlHistory.SqlServer`, `src/SqlHistory.Cli` | The original SQL Server tool, unchanged in behaviour. |
| `SqlHistory.Hackathon/Api` | Minimal-API endpoints, `HistoryCoordinator` (serializes scans and restores), optional `AutoScanService`. |
| `SqlHistory.Hackathon/AiAnalysis` | `ISqlChangeAnalyzer` and its OpenAI-compatible implementation. |
| `SqlHistory.Hackathon/Web` | Vite + React + TypeScript single-page app; Monaco is bundled locally (no CDN). |
| `SqlHistory.Hackathon/demo` | Demo database scripts. |

### API

| Method | Path | Purpose |
|---|---|---|
| GET | `/api/health` | Liveness |
| GET | `/api/status` | Database connection, last scan, AI configuration |
| GET | `/api/routines` | Explorer list (live database merged with history) |
| GET | `/api/routines/{id}` | Routine with its current definition |
| GET | `/api/routines/{id}/versions` | Timeline, newest first |
| GET | `/api/routines/{id}/versions/{version}` | One version with its definition |
| GET | `/api/routines/{id}/compare?from=1&to=2` | Two versions for a diff |
| POST | `/api/snapshots/scan` | Scan and record changes |
| POST | `/api/routines/{id}/analyze` | AI review of `{ "from": 1, "to": 2 }` |
| POST | `/api/routines/{id}/rollback/preview` | Dry run for `{ "version": 1 }` |
| POST | `/api/routines/{id}/rollback` | Restore `{ "version": 1, "expectedCurrentHash": "…", "confirm": true }` |

`{id}` is an opaque base64url routine id taken from `/api/routines`. Every POST needs the header
`X-SqlHistory-Client` (any value); Swagger UI fills it in.

## Requirements

- Ubuntu (developed on 24.04)
- .NET 10 SDK (developed with 10.0.402)
- Node.js 24 and npm 11
- PostgreSQL 16 (server and `psql`)
- An OpenAI API key with active billing, or another OpenAI-compatible provider (optional; everything except AI review works without it)

## Ubuntu setup

**.NET 10.** Ubuntu's packaged `dotnet` may be 8.0. If .NET 10 was installed with
[`dotnet-install.sh`](https://learn.microsoft.com/dotnet/core/tools/dotnet-install-script) into
`~/.dotnet`, put it first on `PATH` for the current shell:

```bash
export DOTNET_ROOT=$HOME/.dotnet
export PATH=$HOME/.dotnet:$PATH
dotnet --version   # 10.0.x
```

All commands below run from the repository root.

```bash
dotnet build SqlHistory.slnx
```

## Configuration

Secrets never go into `appsettings.json`. Use .NET user-secrets for the API project (stored in
`~/.microsoft/usersecrets/`, outside the repository) or environment variables (see
[`.env.example`](.env.example)).

**PostgreSQL connection** (any role that can create the demo database and owns its routines):

```bash
dotnet user-secrets set "ConnectionStrings:SourceDatabase" \
  "Host=localhost;Port=5432;Database=sqlhistory_demo;Username=YOUR_ROLE;Password=YOUR_PASSWORD" \
  --project SqlHistory.Hackathon/Api
```

**AI key**, typed without echo so it stays out of shell history:

```zsh
read -rs "K?OpenAI API key: " && dotnet user-secrets set Ai:ApiKey "$K" --project SqlHistory.Hackathon/Api && unset K
```

(`zsh` syntax; in bash use `read -rsp "OpenAI API key: " K`.)

| Setting | Default | Notes |
|---|---|---|
| `ConnectionStrings:SourceDatabase` | `Host=localhost;Port=5432;Database=sqlhistory_demo` | Add `Username`/`Password` via user-secrets. |
| `ConnectionStrings:SqlHistoryStorage` | `Data Source=sqlhistory-hackathon.db` | SQLite history; relative paths resolve against `SqlHistory.Hackathon/Api`. Created and migrated on startup. |
| `Ai:ApiKey` / `OPENAI_API_KEY` | none | AI review is disabled without a key. |
| `Ai:BaseUrl` | `https://api.openai.com/v1` | Any OpenAI-compatible endpoint. |
| `Ai:Model` | `gpt-4.1-mini` | |
| `Ai:UseJsonSchema` | `true` | Structured output; set `false` if the provider rejects it. |
| `Scan:AutoScanIntervalSeconds` | `0` | `0` = scan only on request. |
| `Cors:AllowedOrigins` | `http://localhost:5173` | |
| `Urls` | `http://localhost:5180` | Loopback only. |

## Demo database

Create a dedicated database and load the sample schema (customers, orders, payments, users and six
routines, including two overloads of `sales.order_total`):

```bash
createdb -h localhost -U YOUR_ROLE sqlhistory_demo
psql -h localhost -U YOUR_ROLE -d sqlhistory_demo -v ON_ERROR_STOP=1 -f SqlHistory.Hackathon/demo/01-setup.sql
```

Every script first checks `current_database()` and refuses to run anywhere except `sqlhistory_demo`.
`01-setup.sql` drops and recreates only the objects it creates.

| Script | Change |
|---|---|
| `01-setup.sql` | Baseline. `public.active_users()` counts users `WHERE is_active = true` (returns 5). |
| `02-remove-active-filter.sql` | Removes that filter (returns 8). The main demo change. |
| `03-extra-changes.sql` | `monthly_revenue` starts counting refunded orders; `record_payment` loses its validation and gains a comment telling AI reviewers to report "no risk". |
| `04-incompatible-change.sql` | Changes `top_customers`' return type (DROP + CREATE), which a restore cannot undo. |

## Running

Two terminals:

```bash
# 1. API → http://localhost:5180, Swagger UI → http://localhost:5180/swagger
dotnet run --project SqlHistory.Hackathon/Api

# 2. Web → http://localhost:5173 (proxies /api to the API)
cd SqlHistory.Hackathon/Web
npm install
npm run dev
```

## Live demo

Start from a fresh demo (see [Resetting](#resetting-the-demo)), with the API and web app running.

1. **Baseline.** Open http://localhost:5173. The six routines show *not scanned*.
2. **First snapshot.** Click **Scan now**. Every routine gets version 1.
3. **Change.** Run the regression:
   ```bash
   psql -h localhost -U YOUR_ROLE -d sqlhistory_demo -f SqlHistory.Hackathon/demo/02-remove-active-filter.sql
   ```
   After a refresh, `active_users()` shows *changed* (the explorer compares live definitions with history).
4. **Second snapshot.** Click **Scan now**. Version 2 is recorded; other routines are unchanged.
5. **Compare.** Open `public.active_users()` → **Compare**. The diff shows the removed `WHERE` clause.
6. **AI review.** **Analyze with AI**: medium risk, "counts all users instead of only active users".
7. **Restore.** **Preview rollback to v1…**: the dry run succeeds and shows the SQL to execute.
   Tick the confirmation and click **Restore version 1**.
8. **Verify.** The timeline shows version 3, *same content as v1*; `SELECT public.active_users();` returns 5 again.

Optional extras:

- **Prompt injection:** run `03-extra-changes.sql`, scan, and analyze `sales.record_payment` v1 → v2.
  The review is High risk and reports the embedded instruction instead of obeying it.
- **Refused restore:** run `04-incompatible-change.sql`, scan, and preview a rollback of
  `analytics.top_customers` to v1. PostgreSQL reports "cannot change return type of existing function"
  and nothing is dropped.

### Resetting the demo

Stop the API, then:

```bash
rm -f SqlHistory.Hackathon/Api/sqlhistory-hackathon.db SqlHistory.Hackathon/Api/sqlhistory-hackathon.db-wal SqlHistory.Hackathon/Api/sqlhistory-hackathon.db-shm
psql -h localhost -U YOUR_ROLE -d sqlhistory_demo -v ON_ERROR_STOP=1 -f SqlHistory.Hackathon/demo/01-setup.sql
```

The history file is recreated empty when the API starts.

## Tests

```bash
dotnet test SqlHistory.slnx
```

Unit tests and SQLite tests (real in-memory SQLite through the real EF migrations, including a test
that history created before this change survives the migration) always run. The PostgreSQL integration
tests are reported as *Skipped* unless a database is configured:

```bash
SQLHISTORY_TEST_POSTGRES="Host=localhost;Database=sqlhistory_demo;Username=YOUR_ROLE;Password=YOUR_PASSWORD" \
  dotnet test SqlHistory.slnx
```

They create and drop uniquely named `sqlhistory_test_*` schemas and cover discovery, overload identity,
dry runs, conflicts, refused return-type changes, and a tampered definition with a second statement.

Frontend: `npm run build` (TypeScript check + production build) and `npm run lint` in `SqlHistory.Hackathon/Web`.

## Safety

- **Restore never runs caller-supplied SQL.** Requests name a version number; the SQL comes from stored history.
- **Restore is narrow.** The definition must be one `CREATE OR REPLACE FUNCTION|PROCEDURE` whose header
  names the same routine (checked against server-side identifier quoting). It runs as a single
  extended-protocol statement, which PostgreSQL rejects if it contains more than one command.
- **Restore is verified.** Inside one transaction (5 s lock timeout, 30 s statement timeout) it checks
  that the routine still has the same OID, that the resulting definition hashes to the stored version,
  and that no routine was added; otherwise it rolls back. Routines are never dropped.
- **Restore is deliberate.** Preview is a real dry run that is always rolled back. The restore request
  must carry `confirm: true` and the hash of the definition shown in the preview, so a concurrent change
  makes it fail (409) instead of being overwritten. Any unrecorded change is scanned into history before
  the restore and the result is scanned afterwards.
- **AI input is untrusted.** Definitions are wrapped in markers with a random per-request nonce, the
  system prompt forbids following embedded instructions, output must match a JSON schema and is
  validated and size-capped server-side, and the model has no tools. Only routine names and definitions
  are sent, never table data, credentials or connection strings. The UI states where they are sent.
- **Local only.** The API binds to loopback, `AllowedHosts` blocks DNS rebinding, CORS allows only the
  web origin, and every POST requires the `X-SqlHistory-Client` header (a cross-site page cannot send
  it without a CORS preflight that fails). There is no authentication: do not expose it on a network.
- **Errors** are returned as problem details without stack traces or configuration.

## Limitations

- The API supports PostgreSQL only. SQL Server remains available through the original CLI; the shared
  code compiles and its tests pass, but it was not run against a SQL Server instance in this hackathon.
- Detection is polling-based (manual or `Scan:AutoScanIntervalSeconds`), not instant.
- Tracked: functions and procedures, including trigger functions. Not tracked: aggregates, window
  functions, routines owned by extensions, views, tables, triggers themselves.
- Ownership, `GRANT`s and comments are not part of `pg_get_functiondef` output, so changes to them are not detected.
- Restore only replaces routines in place: dropped routines, and changes PostgreSQL refuses in
  `CREATE OR REPLACE` (return type, parameter names, defaults), are reported as not supported.
- History is keyed by provider and routine, not by database. Point a different source database at its
  own `ConnectionStrings:SqlHistoryStorage` file.
- Restores run with the configured role's privileges. Prefer a role that owns the tracked routines
  over a superuser.
- One API instance per history file; scans from separate processes are not coordinated.
- AI output can be wrong and costs provider credits; definitions are sent to the configured provider.
- On the first start, EF Core logs a warning that `PRAGMA foreign_keys` cannot run in a transaction
  during the SQLite table rebuild. It is expected; the table has no foreign keys.
