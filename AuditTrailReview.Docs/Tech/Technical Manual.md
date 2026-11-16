# Technical Manual v0.9 

## Purpose and scope
This is the "day 2" companion to `Installation Manual.md`. The Installation Manual gets GxPAuditTrailReview running for the first time; this document covers what's needed to keep it running afterward: the full configuration surface, upgrading, backup/restore, secret rotation, and where to look when something's wrong. It doesn't repeat `TechStack.md` (what the system is built from), and it doesn't repeat day-to-day application usage (creating systems, reviewing scans, signing off); see the in-app User Manual for that.

---

## 1. Components and where they run

| Component | What it is | Default local URL / notes |
|---|---|---|
| `AuditTrailReview.WebApp` | Razor Pages web app | `https://localhost:7190` / `http://localhost:5059` (dev); container exposes `8080`/`8081` |
| `AuditTrailReview.IntegrationHub.API` | HTTP API, API-key authenticated | Container exposes `8080`/`8081`; Swagger UI at `/swagger` in Development only |
| `AuditTrailReview.IntegrationHub.WorkerService` (hosts the Worker Service) | Background host: polls FTP for new files and processes queued large uploads (`ScanUploadJob`, `Tier.Worker`) one at a time | No HTTP endpoint; runs as a background process/service (a Windows Service via the installer, or a container). Must run wherever the API runs, or API uploads stay `Queued` |
| `AuditTrailReview.Console` | CLI client | Not hosted; invoked on demand, no Dockerfile |
| `AuditTrailReview.McpServer` | ASP.NET Core host, read-only Model Context Protocol (MCP) server for QA data exploration from a chat client | `https://localhost:59161` / `http://localhost:59162` (dev, see its `launchSettings.json`); ships a `Dockerfile` and is wired into `docker/docker-compose.postgres.yml` as the `mcp` service (port 8082). `GET /health` returns 200 without a key. Tools at `POST /mcp`; a bare `GET /mcp` (e.g. typing the URL into a browser) correctly returns `405`, not a bug -- see section 7 |

All five components connect to the same database: SQL Server is the supported deployment for all five. `docker/docker-compose.sqlite.yml` and `docker/docker-compose.postgres.yml` provide evaluation-only flavors (`Database:Provider` = `Sqlite` / `Postgres`) for the first four; `AuditTrailReview.McpServer` honours the same `Database:Provider` switch (the Postgres compose file runs it as the `mcp` service; see ADR-0010). Only `AuditTrailReview.McpServer` exposes a health-check endpoint (`GET /health`); the other four do not; see section 6.

---

## 2. Configuration reference

Every component reads configuration the standard ASP.NET Core way: `appsettings.json`, overridden by `appsettings.Development.json` when `ASPNETCORE_ENVIRONMENT=Development`, overridden by environment variables (`Section__Key` syntax) or `dotnet user-secrets` in development. None of the five components currently integrate with an external secret store such as Azure Key Vault; see section 5. (In the Azure Container Apps deployment, `pipelines/pipeline-deploy-on-azure-cloud.yml` stores `Security:EncryptionKey` and the initial admin password in Key Vault and Container Apps hand them to the apps as ordinary environment variables through Key Vault references; the apps themselves still just read configuration. The apps reach Azure SQL with a managed identity, so no SQL password exists. See `CICD/Azure Pipelines.md`.)

### AuditTrailReview.WebApp
| Key | Purpose | Notes |
|---|---|---|
| `ConnectionStrings:DefaultConnection` | Database connection string | Shared by every component that connects to the database |
| `Database:Provider` | `SqlServer` (default), `Sqlite` or `Postgres` | Only `SqlServer` is the supported path for real GxP use. The other two are set by `appsettings.Sqlite.json` / `appsettings.Postgres.json` (selected with `ASPNETCORE_ENVIRONMENT`) and create the schema from the EF Core model at startup rather than from the SQL scripts in section 4 |
| `SeedAccount:Email` / `SeedAccount:Password` | First Admin account, created on startup against an empty user table | Only takes effect once; if a user already exists, this is ignored |
| `OpenAI:ApiKey` (or `OpenAIApiKey`) | OpenAI API key | Used for the background AI analysis step that runs after every scan. (The key and model can also be set from the AI Analysis tab and are then stored encrypted in the database.) Keep the real key in user secrets or an environment variable (`OpenAI__ApiKey`), never in a tracked `appsettings.json`. A missing or invalid key doesn't fail the upload; the scan simply has no result on its AI Analysis tab (check the WebApp logs for the actual error) |
| `SmtpSettings:Host/Port/EnableSsl/SenderEmail/SenderName/UserName/Password` | Outgoing email for the admin "Send Credentials" feature | Optional; only that one feature needs it |
| `Logging:LogLevel:*` | Standard ASP.NET Core logging | Default: `Information` (`Microsoft.AspNetCore` at `Warning`) |
| `Serilog:*` | Structured logging: console output plus a `dbo.ApplicationLog` SQL Server table | See section 6. The MSSqlServer sink's `connectionString` setting reuses `ConnectionStrings:DefaultConnection` above by name |
| `AllowedHosts` | ASP.NET Core host filtering | Default `*` |

### AuditTrailReview.IntegrationHub.API
| Key | Purpose | Notes |
|---|---|---|
| `ConnectionStrings:DefaultConnection` | SQL Server connection string | Same database as the WebApp and the Worker Service |
| `ApiKeyAuth:HeaderName` | Header callers must send | Default `X-Api-Key` |
| `Security:EncryptionKey` | Master key (base64, 32 bytes) for decrypting stored API keys | Must be byte-identical to the WebApp's own `Security:EncryptionKey` -- this process only decrypts keys to validate requests, it never writes them |
| ~~`ApiKeyAuth:Keys`~~ | Removed. Accepted keys now live encrypted in `dbo.ApiKeyCredential`, managed from the WebApp's AdminPanel -> Settings -> API tab (generate/revoke), not in `appsettings.json` | Every key still carries the same `scan:write` permission; there's no per-key identity beyond that. Any caller still sending the old shipped default (`change-me-api-key`) gets `401` after upgrading -- generate a real key and redistribute it |
| `RateLimiting:PermitLimit` / `RateLimiting:WindowSeconds` | Rate limit | Default 60 requests per 60 seconds per caller (partitioned by `X-Api-Key` value, or by remote IP if no key is presented). Raise `PermitLimit` if a legitimate integration is being rate-limited |
| Upload size limit | `[RequestSizeLimit]`/`[RequestFormLimits]` on `ScanProcessingController` | 786,432,000 bytes (750 MiB). Every accepted upload is stored as a `Tier.Worker` `ScanUploadJob` (`202 Accepted`) and processed by the Worker Service, which must be running for jobs to complete. Not configurable via `appsettings.json`; changing it means editing the controller attribute (and, if the API is ever IIS-hosted, its `web.config`'s `maxAllowedContentLength` to match) |
| `Logging:LogLevel:*` | As above | |
| `Serilog:*` | Same pattern as the WebApp: console output plus the same `dbo.ApplicationLog` table, distinguished by the `Application` column | See section 6 |

### AuditTrailReview.IntegrationHub.WorkerService (Worker Service)
| Key | Purpose | Notes |
|---|---|---|
| `ConnectionStrings:DefaultConnection` | SQL Server connection string | Same database as the WebApp and the API |
| FTP settings (Admin Panel > Application Settings > FTP) | Stored in `dbo.FtpConfiguration` (one row), not in `appsettings.json`. Master switch `Enabled` (off by default), host, port, transport security (None / Explicit FTPS / Implicit FTPS), certificate validation and optional pinned thumbprint, username, password (encrypted with `Security:EncryptionKey`), target system, remote folder, processed/failed subfolders, polling interval, stale-file threshold and check interval | Re-read by the Worker on every poll, so changes need no restart. A single Worker instance ingests for exactly one system. The FTP account needs write, rename and create-directory permission in the remote folder to move files. Setting a subfolder blank disables that move |
| `Serilog:*` | Same console plus `dbo.ApplicationLog` pattern as the other two components | See section 6 |

### AuditTrailReview.McpServer
| Key | Purpose | Notes |
|---|---|---|
| `ConnectionStrings:DefaultConnection` | SQL Server connection string | Same database as the other four components; SQL Server only today, no `Database:Provider` switch |
| `McpAuth:HeaderName` | Header carrying the key | Default `X-Api-Key`; `Authorization: Bearer <key>` is accepted as well |
| `Security:EncryptionKey` | Master key (base64, 32 bytes) for decrypting stored MCP keys | Must be byte-identical to the WebApp's own `Security:EncryptionKey` -- this process only decrypts keys to validate requests, it never writes them |
| ~~`McpAuth:Keys`~~ | Removed. Accepted keys now live encrypted in `dbo.McpApiKeyCredential`, managed from the WebApp's AdminPanel -> Settings -> MCP tab (generate/revoke), not in `appsettings.json` | A deliberately separate key pool from `IntegrationHub.API`'s own "API" tab -- a key from one never authenticates the other. Any caller still sending the old shipped placeholder (`CHANGE_ME_MCP_API_KEY`) gets `401` after upgrading -- generate a real key and redistribute it |
| `McpRateLimiting:PermitLimit` / `WindowSeconds` | Fixed-window limit per caller IP | Defaults 60 requests per 60 s; over the limit returns 429. `/health` is exempt |
| `Logging:LogLevel:*` | Standard ASP.NET Core logging | Console output only -- no Serilog, no `dbo.ApplicationLog` integration (see section 6) |

`GET /health` returns 200 without a key (liveness probe for container platforms). A static key carries no per-user identity, and the HTTPS endpoint uses the plain ASP.NET Core dev certificate locally, which an MCP client's own TLS stack may not trust even when a browser accepts it (see section 7). For a hosted deployment use a properly issued certificate, supply the key from a secret store, and connect with a read-only database login (ADR-0010).

### AuditTrailReview.Console
This project has its own small per-instance `appsettings.json` sitting next to the exe (auto-created with placeholder values on first run if missing), read directly with `System.Text.Json` rather than the `Microsoft.Extensions.Configuration` stack the other three components use. This is deliberate, to keep this project's footprint minimal for standalone deployment to many different machines, including older ones. Configuration is file-only; there is no environment-variable fallback.

| Source | Key | Purpose |
|---|---|---|
| `appsettings.json` (`ScanApi:BaseUrl`) | Base URL of the Integration Hub API | Placeholder value `CHANGE_ME_API_BASE_URL` is treated as "not configured" (exact match, not a prefix check) |
| `appsettings.json` (`ScanApi:ApiKey`) | A key generated from the webapp's `AdminPanel -> Settings -> API` tab (`dbo.ApiKeyCredential`) | Placeholder value `CHANGE_ME_API_KEY` is treated as "not configured" (exact match, not a prefix check) |
| `appsettings.Development.json` (same two keys) | Local Development override of the two settings above | Optional, gitignored like every other component's own Development file, read only when `DOTNET_ENVIRONMENT=Development`; a non-placeholder value here wins over the base file's. `Properties/launchSettings.json` sets `DOTNET_ENVIRONMENT=Development` for local `dotnet run`/F5 sessions |

If `appsettings.json` doesn't exist at all when the app starts, it creates one with placeholder values, prints where it was written, and continues. `appsettings.Development.json` is never auto-created; it's an optional, developer-supplied override only.

**Logging:** uses Serilog too, but scoped narrower than the other three components. The pipeline (console plus a rolling local file, `Logs/console-.log`) is built directly in code in `Program.cs` (this component's `appsettings.json` is its own `ScanApi` config, not a Serilog-shaped one), and there is no `Serilog.Sinks.MSSqlServer` sink; nothing from this component is persisted to `dbo.ApplicationLog`.

### Application-level settings (stored in the database, not appsettings)
These are configured through the WebApp UI (Config panel), not files:

- 19 rule enable/disable flags (`Rules` table), grouped into 5 categories: Data Integrity, Action Consistency, Temporal Checks, User Validation, Anomaly Detection.
- `EngineSettings` (one row, applies globally): `WorkdayStartHour`/`WorkdayEndHour` (default 7/18), `AllowWeekends` (default false), `WorkdayTimeZone` (an IANA time zone ID the workday check is evaluated against; empty by default, which falls back to UTC), `UsernameMinLength` (default 3), `AdditionalSystemAccountPatterns`, `MassChangeThresholdCount`/`MassChangeWindowSeconds` (default 10 records within 1 second), `AiAnalysisEnabled` (default true; turns the background AI analysis step for manual uploads on or off).

These settings apply globally, immediately, to every ingestion path; there's no per-environment or per-system override.

---

### Large-file processing: memory and provider behavior

- **Stored upload read.** `ScanUploadJobProcessor` never loads `ScanUploadJob.FileContent` through Entity Framework change tracking. On SQL Server and PostgreSQL, every status transition is a set-based update naming only the columns it touches, and the stored bytes are read as a stream on a second connection of the same provider. Only EF Core InMemory (used by the automated tests) uses the tracked-entity fallback.
- **Still in memory.** `ScanProcessingService` reads the uploaded file into one string, builds one object per row while parsing and mapping it, and adds the resulting findings to the change tracker before saving. Peak memory therefore scales with the number of rows in the file, not with how it's streamed in; see `Installation Manual.md` section 1 ("Hardware sizing") for the machine a very large file was benchmarked against.
- **One job at a time.** The Worker Service processes `Tier.Worker` jobs sequentially, so this memory is used by one file at a time.
- **PostgreSQL flavor.** `docker/docker-compose.postgres.yml` runs Postgres, the WebApp, the API and the Worker Service together, sharing the Docker VM's memory limit. It is a functional-testing flavor, not a performance benchmark.

---

---

### MCP server (QA data exploration) operations

- **What it is:** `AuditTrailReview.McpServer` exposes scan/system data to a chat client (e.g. Claude) over the Model Context Protocol (MCP), for QA to explore audit-trail data conversationally. See ADR-0010 for why it's a separate component from the Integration Hub API.
- **Tools (as of v0.9):** `list_systems` (every configured system) and `list_scans` (most recent scans, optionally filtered by `systemId`, capped at 200 rows per call -- a context-window protection, not a security control).
- **Connecting:** Streamable HTTP transport, stateless mode (`HttpServerTransportOptions.Stateless = true`), tools at `POST /mcp`. No `Mcp-Session-Id` is required. A bare `GET /mcp` -- what happens when the URL is typed into a browser -- correctly returns `405 Method Not Allowed`; MCP over HTTP isn't browsable the way a REST API with Swagger is. Use an actual MCP client, or the `@modelcontextprotocol/inspector` dev tool (`npx @modelcontextprotocol/inspector`, connect with transport "Streamable HTTP"), to explore or call the tools interactively.
- **HTTPS locally:** the dev `launchSettings.json` exposes both `https://localhost:59161` and `http://localhost:59162`. The HTTPS endpoint uses the ASP.NET Core dev certificate, which some MCP clients' own TLS stacks reject even though a browser accepts it (observed as a generic `fetch failed` from Claude Code) -- use the plain HTTP endpoint for local/dev use, and a properly issued certificate (not the dev cert) for any real deployment.
- **Security posture:** strictly read-only (every tool is `AsNoTracking`, the project never calls `SaveChanges`) and `/mcp` requires an API key (generated from AdminPanel -> Settings -> MCP, stored encrypted in `dbo.McpApiKeyCredential`) and is rate limited per IP. A static key has no per-user identity; use a read-only database login for this component.

---

## 3. First-run seeding behavior

On startup, `AuditTrailReview.WebApp` seeds baseline data only if it isn't already present (each seed step checks whether its table is empty, so re-running against an already-seeded database is safe and a no-op):

- The three Identity roles (Admin, Super User, User): created if missing.
- The `SeedAccount` Admin user: created only if the `AspNetUsers` table is completely empty.
- 19 rules across 5 categories (all enabled): created only if `RuleCategories` is empty.
- 8 default system types (LIMS, MES, CDS, ERP, QMS, SCADA, DMS, ELN): created only if `SystemType` is empty.
- A default `EngineSettings` row: created only if none exists.

Only the WebApp performs this seeding; the API and Worker Service assume it's already been done. They'll work against an unseeded-but-schema-present database, but with every rule disabled and no findings generated; check **Config → Rule-Based Review Engine** after a fresh install if a Worker Service- or API-ingested scan comes back with zero findings unexpectedly.

---

## 4. Upgrading

There is no automated upgrade path: nothing calls `Migrate()` or `EnsureCreated()` against SQL Server, and there is no `dotnet ef database update` step. The schema is distributed as raw SQL scripts in `AuditTrailReview.Database/Database schema/` of two kinds. **Full snapshots** (for example `ATR 0.8 20261020-SQL2025.sql`, with a `-SQL2012` variant for SQL Server 2012 compatibility) create the whole database from nothing and cannot be re-run against a database that already has data. **Incremental scripts** (numbered `ATR 0.20` and above, up to `ATR 0.40` at v1.0) are small `ALTER`/`CREATE` deltas applied on top of the `ATR 0.8` snapshot, in version order. Sort by the version number in the filename, not alphabetically (`0.7` sorts after `0.20`). `Installation Manual.md` section 3 lists every incremental script and what it changes.

Until an incremental migration tool exists, treat every schema change as a manual change-control exercise:

1. **Back up the production database first** (see section 5); there's no rollback mechanism other than restoring from backup.
2. **Find which incremental scripts your database is missing.** There is no version table, so compare your schema against the list in `Installation Manual.md` section 3 (for example, check whether `dbo.FtpConfiguration` exists for `0.40`) and apply every missing script in version order. If your database predates the `ATR 0.8` snapshot, compare against that snapshot with a SQL schema-compare tool and write the `ALTER` statements yourself.
3. **Review and apply those statements to a copy of production first**, and confirm the application starts against it before touching the real database. If the change affects existing data, run it in a maintenance window.
4. **Deploy the new application version(s)** (WebApp, API, Worker Service, and McpServer if deployed; check the release notes for which changed) after the schema change is applied, not before.
5. **Let the WebApp's startup seeding run.** It's safe against a database that already has seeded reference data (see section 3), so this step doesn't need special handling. New seed data (for example, a newly added rule) only appears if the corresponding table's "is it empty" check still considers the table empty enough to trigger; check the seeding code in `Program.cs` for the specific new version if a new rule or system type doesn't appear after upgrade.

---

## 5. Backup, restore, and secrets

### Database backup/restore
Standard SQL Server backup/restore applies; there's nothing application-specific here, because all durable state (scans, findings, signatures, rule configuration, logs, Identity accounts) lives in the one shared database:

```sql
BACKUP DATABASE GxPAuditTrail TO DISK = 'GxPAuditTrail.bak' WITH INIT;
-- ...
RESTORE DATABASE GxPAuditTrail FROM DISK = 'GxPAuditTrail.bak' WITH REPLACE;
```

Schedule this at the infrastructure level (a SQL Server Agent job, a managed-SQL backup policy, etc.); nothing in the application itself triggers or manages backups.

**Worker Service "already processed" tracking and database restores.** The Worker Service's already-processed tracking lives in a dedicated `dbo.IngestedFile` table, not in memory, so it's covered by a database backup along with everything else. One consequence follows directly: restoring the database to an earlier point in time also rolls back which files the Worker Service considers claimed or completed. After such a restore, expect the Worker Service to re-ingest (as new scans) any FTP files that were originally processed between the backup point and now, if they're still on the FTP server. If the restored backup happened to be taken mid-file (an `IngestedFile` row still `Processing`), the Worker Service's own startup crash-recovery resets it to `Pending` automatically, the same as it would after an ungraceful shutdown; no manual cleanup needed.

A file's location on the FTP server (whether it's still in `RemoteFolder`, or has already been moved into `ProcessedFolder`/`FailedFolder`) is not part of the database and so is untouched by a restore. If the database is restored to before a file was completed, but that file was already moved out of `RemoteFolder` on the FTP server by the time of the restore, the Worker Service's next poll simply won't see it in the listing at all; it won't be silently duplicated or error out, it just won't be re-ingested. If that file's data genuinely needs to come back after such a restore, move it back into `RemoteFolder` manually first.

### Secret rotation
| Secret | Where it lives | How to rotate |
|---|---|---|
| OpenAI API key | WebApp `OpenAI:ApiKey` | Generate a new key in the OpenAI account, update config, restart the WebApp. Manual uploads keep working; only the background AI analysis step is affected until this is valid, see section 7. |
| `Security:EncryptionKey` (master key for: the OpenAI key stored via the AI Analysis tab, the SMTP password stored via the Email tab, and -- as of 2026-11-05 -- every API key generated from the "API"/"MCP" tabs, all via `ApiKeyProtector`, AES-256-GCM; named `Security:AiApiKeyEncryptionKey` before the Email tab started using it too) | WebApp, Worker Service, API, and McpServer -- all four now read `Security:EncryptionKey` | A base64-encoded, random 32-byte value; the shipped `appsettings.json` placeholder (`CHANGE_ME_32_BYTE_BASE64_KEY`) is **not** valid and must be replaced before using the AI Analysis tab's or Email tab's "store in the database" option (or saving throws `InvalidOperationException`/`500`, see section 7), and before any API/MCP key will validate at all (a missing/invalid key makes every request fail with `401`, not `500`). On the PostgreSQL Docker flavor this comes from the gitignored `.env` file (`ENCRYPTION_KEY`, see `.env.example` and `CICD/Run docker compose on VPS.md` section 3) rather than being hardcoded in `docker/docker-compose.postgres.yml` -- a working AES-256 key would be indistinguishable from a real leaked secret to anything scanning the repository. Must be set identically on all four components -- whichever one encrypted a stored value, the others have to be able to decrypt it. Rotating it without migrating the already-stored ciphertext first makes every existing stored secret (OpenAI key, SMTP password, every API/MCP key) undecryptable; re-save/regenerate from each one's own tab after rotating. |
| SMTP credentials | WebApp `SmtpSettings:*` | Update and restart; only affects the "Send Credentials" email feature. |
| FTP credentials | Admin Panel > Settings > FTP | Enter the new password and save; the Worker picks it up on its next poll. |
| Integration Hub API key(s) | `dbo.ApiKeyCredential`, managed from `AdminPanel -> Settings -> API` | Generate a new key, update all callers (the Console client, external systems) to use it, then revoke the old key once callers are confirmed migrated -- no restart needed either way, and nothing to edit in `appsettings.json`. |
| MCP server API key(s) | `dbo.McpApiKeyCredential`, managed from `AdminPanel -> Settings -> MCP` | Same as above, but on the "MCP" tab -- this is a separate key pool from the Integration Hub API's; a key from one tab does not work on the other. |
| SQL Server credentials | Each component's `ConnectionStrings:DefaultConnection` | Coordinate across all four database-connecting components (WebApp, API, Worker Service, McpServer): they share one database and must be updated together. |
| `SeedAccount:Password` | WebApp config | Only affects first-run seeding; changing it after the account already exists has no effect. Rotate that account's password through the normal Identity password-reset flow instead. |

Secrets that remain configuration (the OpenAI key, SMTP credentials, FTP credentials, `Security:EncryptionKey` itself) should only ever live in `dotnet user-secrets`, an environment variable, or a gitignored `appsettings.Development.json`, never in a tracked `appsettings.json`. The Integration Hub API and MCP server keys are no longer configuration at all -- they live encrypted in the database and are managed entirely from the AdminPanel UI. There is currently no integration with an external secret store such as Azure Key Vault in any component.

---

## 6. Monitoring and health

Only `AuditTrailReview.McpServer` has a health endpoint: `GET /health` returns `200` with no API key and is exempt from the rate limiter, for container-platform probes. The WebApp, the API, the Worker Service and the Console have no health-check endpoint, and nothing integrates with an APM/monitoring product (Application Insights, Prometheus, etc.). Logging is the primary signal.

Four of the five components use Serilog, but not identically. The WebApp, the Worker Service, and the Integration Hub API persist Warning-and-above events to a `dbo.ApplicationLog` table in the shared SQL Server database, in addition to console output; the WebApp and the API additionally log one summarized entry per HTTP request, since both are ASP.NET Core applications. None of the three keep a local log file as a fallback, so a sink failure (for example, an unreachable database, or a missing column) fails silently, with nothing local to check. `AuditTrailReview.Console` also uses Serilog, but writes only to its own console session and a local rolling file (`Logs/console-.log`), since it has no database connection to log to. `AuditTrailReview.McpServer` is the fifth component and does **not** use Serilog at all: it logs only through the default ASP.NET Core console logger, with nothing persisted to a database or a local file.

**Before relying on `dbo.ApplicationLog`, confirm the table has an `Application nvarchar(128) NULL` column.** The shipped schema scripts in `AuditTrailReview.Database/Database schema/` create `ApplicationLog` without this column, but all three components' Serilog configuration expects it to already exist (their `autoCreateSqlTable` setting is `false`), so each component's rows can be told apart. Add the column by hand if it's missing:

```sql
ALTER TABLE dbo.ApplicationLog ADD [Application] nvarchar(128) NULL;
```

For the components without a health endpoint, the practical way to check each is healthy:
- **WebApp**: the dashboard loads and shows updated counts.
- **API**: `GET /swagger` responds in Development, or a `POST` with a deliberately invalid API key returns `401` (proves the process is up and the authentication pipeline is running); the API has no unauthenticated `/health` endpoint to hit instead.
- **Worker Service**: check its logs for the periodic "Checking FTP for new audit trail files" line at the configured polling interval; absence of that line for longer than the interval means the process has stopped or hung.
- **MCP server**: `GET /health` returns `200`; for a deeper check, a `POST /mcp` `initialize` JSON-RPC request returns `200` with the server's capabilities; a bare `GET /mcp` deliberately returns `405` (see section 7), so that alone doesn't indicate a problem.

### Support runbook: querying `dbo.ApplicationLog` (WebApp, Worker Service, Integration Hub API)

This is a plain SQL Server table; any SQL client (SSMS, Azure Data Studio, etc.) connected to the shared database works. Confirm you're looking at the right database first: `ConnectionStrings:DefaultConnection` can differ between `appsettings.json` and `appsettings.Development.json`, and may differ again in production; check whichever `appsettings*.json` (or environment variable override) is actually in effect for the instance you're investigating.

**Columns:** `Id` (row number, no meaning beyond ordering), `TimeStamp` (UTC), `Level` (`Information`/`Warning`/`Error`/`Fatal`; only `Warning` and above are ever written here), `Message` (the rendered log line), `MessageTemplate` (the unrendered version, for example `"AI analysis failed for scan #{ScanId}"`), `Exception` (the full stack trace, when the event was logged with one; usually the most useful column for a bug report), `Properties` (XML: every structured value from the message template, plus enrichers like `MachineName`), `CorrelationId` (the originating `Scan.Id` as text, for anything that happened during a manual upload; always `NULL` for Worker Service and Integration Hub API rows, since correlation isn't threaded through either of those ingestion pipelines), `SourceContext` (the logger's category, usually a class name such as `AuditTrailReview.WebApp.Pages.Scan.ScanModel`, `AuditTrailReview.IntegrationHub.WorkerService.Worker`, or `AuditTrailReview.IntegrationHub.API.Controllers.ScanProcessingController`, or an ASP.NET Core internal category for the per-request summary line), `Application` (which component wrote the row; see the note above if this column doesn't exist yet).

**Everyday triage, most recent problems first:**
```sql
SELECT TOP 100 TimeStamp, Level, Message, CorrelationId, SourceContext
FROM dbo.ApplicationLog
ORDER BY TimeStamp DESC;
```

**Only real breakages, not soft/expected warnings** (for example, an AI-analysis timeout is logged as a `Warning` by design and isn't itself a bug):
```sql
SELECT TOP 100 TimeStamp, Message, Exception, CorrelationId
FROM dbo.ApplicationLog
WHERE Level IN ('Error', 'Fatal')
ORDER BY TimeStamp DESC;
```

**Everything in a specific time window** (using the timestamp a user reports; remember `TimeStamp` is UTC, not local time):
```sql
SELECT TimeStamp, Level, Message, Exception, CorrelationId
FROM dbo.ApplicationLog
WHERE TimeStamp >= '2026-09-02T14:00:00' AND TimeStamp < '2026-09-02T15:00:00'
ORDER BY TimeStamp;
```

**Search by keyword** (a user mentions "OpenAI," "upload," a filename, etc.):
```sql
SELECT TOP 100 TimeStamp, Level, Message, CorrelationId
FROM dbo.ApplicationLog
WHERE Message LIKE '%OpenAI%' OR Exception LIKE '%OpenAI%'
ORDER BY TimeStamp DESC;
```

**Trace one upload's whole journey** (ingestion, rule evaluation, the AI call), once you have its Scan Id, from the Scan Details page's URL (`/Scan/Details?id=123`), or from any log row that already mentions `#123`:
```sql
SELECT TimeStamp, Level, Message, Exception, SourceContext
FROM dbo.ApplicationLog
WHERE CorrelationId = '123'
ORDER BY TimeStamp;
```
Cross-reference against the actual scan record with `SELECT * FROM dbo.Scan WHERE Id = 123` to see what it actually did (file name, date, whether it's closed) alongside what got logged about it.

**Look up an error a user reports** (WebApp): the Error page shown for an unexpected problem, a 404 or a bad request prints a *Reference* such as `0HNPRFPVP4LBR:00000001`. Every log event the WebApp writes while serving that request carries the same value as its `CorrelationId` (`Security/RequestCorrelationMiddleware.cs`), so ask the user for the reference and run:
```sql
SELECT TimeStamp, Level, Message, Exception, SourceContext
FROM dbo.ApplicationLog
WHERE CorrelationId = '0HNPRFPVP4LBR:00000001'
ORDER BY TimeStamp;
```
Only `Warning` and above are stored (see *Columns* above), so a 404 or a 400 leaves no row, while an unhandled exception (HTTP 500) leaves an `Error` row with the stack trace. A scan upload overrides the value with the scan id for its own events, as described above.

**Is this trending, or a one-off?**
```sql
SELECT CAST(TimeStamp AS DATE) AS [Day], Level, COUNT(*) AS Occurrences
FROM dbo.ApplicationLog
WHERE Level IN ('Error', 'Fatal')
GROUP BY CAST(TimeStamp AS DATE), Level
ORDER BY [Day] DESC;
```

**If the table has nothing at all despite a known problem, check these before assuming everything's fine:**
- Whether the event was ever going to be `Warning`-level or above in the first place: routine `Information`-level activity (most of what happens in a normal request) is deliberately not persisted here, by design, so its absence isn't itself a problem. (Development environments have this dialed down to `Information` for easier local testing; production stays `Warning`-and-above.)
- Whether the database itself was reachable at the time, and whether the `Application` column actually exists (see the note above). A sink failure (a bad connection string, a missing column, or the database being unreachable) fails completely silently, with nothing local left to check. If `dbo.ApplicationLog` unexpectedly has nothing despite a known problem, first rule out a database-connectivity issue directly using the practical health checks above, since the log itself won't tell you.

**Filter to one component, or see everything together:**
```sql
SELECT TOP 100 TimeStamp, Level, Message, Application, CorrelationId
FROM dbo.ApplicationLog
WHERE Application = 'AuditTrailReview.IntegrationHub.WorkerService'  -- or 'AuditTrailReview.WebApp' / 'AuditTrailReview.IntegrationHub.API'; omit to see all three
ORDER BY TimeStamp DESC;
```

**Scope reminder:** this covers `AuditTrailReview.WebApp`, `AuditTrailReview.IntegrationHub.WorkerService` (the Worker Service), and `AuditTrailReview.IntegrationHub.API`; these are the three components that persist to `dbo.ApplicationLog`. `AuditTrailReview.Console` uses Serilog too, but by design only writes to its own console session and a local `Logs/console-.log` file, not to this table; for a problem there, check that file instead. `AuditTrailReview.McpServer` isn't listed here either, for a different reason: it doesn't use Serilog at all, only the default ASP.NET Core console logger, so there's no file or database table to check for it beyond its own console output.

**No retention or purge policy yet.** Nothing prunes old rows from `dbo.ApplicationLog`; it will grow unbounded until that's addressed. Periodically check row count and age (`SELECT COUNT(*), MIN(TimeStamp), MAX(TimeStamp) FROM dbo.ApplicationLog`) and manually archive or delete old rows if it starts to matter.

---

## 7. Troubleshooting (operational)

This extends the first-run table already in `Installation Manual.md` with issues that show up after go-live rather than during initial setup.

| Symptom | Likely cause / what to check |
|---|---|
| A scan appears to exist twice, with the same file name and very close timestamps, from the Worker Service | Should no longer occur: the Worker Service claims each file in `dbo.IngestedFile` before processing it and checks that table for already-claimed files on each poll, so a restart no longer reprocesses a file that already completed. If it does occur, it likely indicates the database was restored to a point before the file was originally ingested (see the backup/restore note above) or that the file was manually re-uploaded through another path. Confirm via the Worker Service logs and `SELECT * FROM dbo.IngestedFile WHERE FileName = '...'`, then delete the duplicate scan from `Scan/Delete` (or directly in SQL if it has already accumulated review data you don't want to lose from the original). |
| Manual scan uploads succeed, but scans never get an AI Analysis tab result | Almost always the OpenAI key (section 5): it's expired, revoked, rate-limited, or the OpenAI API is down. The AI call runs in the background, so this doesn't fail the upload itself; check the WebApp logs for `AI analysis failed for scan #...` to confirm the cause. Rule-based ingestion via the Worker Service or API doesn't call OpenAI at all, so it's unaffected either way. |
| API calls return `401` unexpectedly | Confirm the caller's key is an active (not revoked) key generated from `AdminPanel -> Settings -> API` (comparison is constant-time but still exact-match on the decrypted value), and that the API's `Security:EncryptionKey` is set and matches the WebApp's own exactly -- a missing/wrong master key makes every key check fail the same way, since `dbo.ApiKeyCredential` rows can't be decrypted to compare against. The same applies to the MCP server's `401`s against `AdminPanel -> Settings -> MCP`/`dbo.McpApiKeyCredential`. |
| API calls return `429 Too Many Requests` | The caller has exceeded `RateLimiting:PermitLimit` requests within `RateLimiting:WindowSeconds`; the response includes a `Retry-After` header. Check the WebApp/API logs for `Rate limit exceeded for ...` to see whether it was a known API key or an unrecognized/missing one; if it's a legitimate integration tripping the limit under normal load, raise `RateLimiting:PermitLimit`. |
| API calls fail with `413`, or the connection is simply reset, for a large file | The uploaded file exceeds the API's 786,432,000-byte (750 MiB) request-size cap. If the caller is `AuditTrailReview.Console`, also confirm its `HttpClient.Timeout` (20 minutes as shipped) hasn't been reduced; a file near that size can take well over ten minutes to process. |
| API calls return `503` | The API's database connection is unavailable; check the SQL Server instance and the API's `ConnectionStrings:DefaultConnection`, not the caller. |
| Worker Service stops picking up new files | Check FTP connectivity/credentials first (Settings > FTP), then check whether the process itself is still running (section 6); a crashed process produces no further "Checking FTP" log lines. If the process is running but one specific file never gets picked up, check `dbo.IngestedFile` for a row with that `FileName`: a `Failed` row with `RetryCount` at the configured cap (3) means the file was retried and given up on rather than silently ignored; see its `ErrorMessage` column for why. If it's still sitting in `RemoteFolder`, fix the underlying issue and delete or reset the row to let it be picked up again; it may already have been moved into the configured `FailedFolder` subfolder (default `Failed`) for review, since a permanently-failed file is relocated there automatically. A `Processing` row that never resolves across multiple polls, while the service is otherwise healthy, is worth investigating directly (the stuck-claim recovery only runs once at startup, not on every poll). |
| A processed or permanently-failed file isn't being moved out of `RemoteFolder` on the FTP server | Check the Worker Service logs for "...move: failed to move..." or "...move: could not move..." warnings; the most common cause is the FTP account not having write, rename, and create-directory permission in `RemoteFolder` (plain read/download access isn't enough). A move failure never affects the scan itself or the file's `IngestedFile` status; it's purely a housekeeping step, so this is safe to leave unresolved if the FTP account genuinely can't be granted write access (set the processed/failed subfolder settings to an empty string to stop attempting the move and silence the warnings). If the move genuinely just failed once and the account does have proper permission, no action is needed: the stale-file sweep below retries it automatically within the stale-file check interval and threshold settings of it becoming eligible. |
| Log shows "Found N file(s) still in ... that should have been moved out ... ago" | This is the stale-unmoved-file sweep (throttled to run at most once every the stale-file check interval) finding and retrying a file whose original Processed/Failed move failed. If it logs once and the file disappears from `RemoteFolder` on the next check, the retry succeeded and no action is needed. If it keeps recurring for the same file across multiple sweeps, that's the same underlying cause as the row above (almost always an FTP permission problem) and is worth fixing directly rather than waiting for the sweep to keep retrying indefinitely. |
| A newly-added rule or system type doesn't appear after an upgrade | The startup seed step for that table only runs if the table is still empty (sections 3 and 4); a rule added in a new version won't retroactively insert itself into an already-seeded `Rules` table. Insert it manually or via a one-off script. |
| Close-and-Sign appears to have gone through with unreviewed findings still present | The "must review everything first" rule is currently enforced in the UI only, not on the server; this is expected current behavior if the action was triggered other than through the normal button, not a bug to chase. |
| Users report they can access rule configuration, engine settings, or system type management despite not being Admin or Super User | Expected current behavior, not a bug. Only the Admin panel's User/Role pages are actually role-restricted today. |
| Any page with a form (e.g. `/Identity/Account/Login`) returns `500`, and the logs show `AntiforgeryOptions.Cookie.SecurePolicy = Always, but the current request is not an SSL request` | The WebApp is running behind a reverse proxy/tunnel that terminates TLS and forwards plain HTTP, but isn't sending the `X-Forwarded-Proto` header `Program.cs`'s `UseForwardedHeaders` middleware needs, or its address isn't in `ForwardedHeaders:KnownProxies`/`ForwardedHeaders:KnownNetworks` (headers from unlisted senders are ignored; default is loopback only). Confirm the proxy sends `X-Forwarded-Proto: https` and add its address/network; see `CICD/Run docker compose on VPS.md` section 6. |
| Saving an OpenAI API key from the AI Analysis tab, or an SMTP password from the Email tab (both Admin Panel -> Application Settings) returns `500` | `Security:EncryptionKey` is missing or still the shipped `appsettings.json` placeholder (`CHANGE_ME_32_BYTE_BASE64_KEY`, which isn't valid base64/32 bytes) -- `ApiKeyProtector` throws `InvalidOperationException` rather than silently storing something unusable. Set a real base64, 32-byte value (see the secret-rotation table above), identically on WebApp and the Worker Service; on the PostgreSQL Docker flavor, set `ENCRYPTION_KEY` in `.env` (`.env.example` has the generation command) and restart both. |
| Typing `AuditTrailReview.McpServer`'s URL into a browser shows an error page / "This page isn't working" / `HTTP ERROR 405` | Expected, not a bug: MCP over Streamable HTTP only accepts `POST` (JSON-RPC); a browser always sends `GET`. Use an actual MCP client or the `@modelcontextprotocol/inspector` dev tool instead of a browser address bar. |
| An MCP client (e.g. Claude Code) fails to connect to `AuditTrailReview.McpServer` with a generic error like `fetch failed`, specifically over `https://` | The ASP.NET Core dev HTTPS certificate isn't trusted by the client's own TLS stack, even though a browser accepts it. Use the plain `http://` endpoint instead for local/dev use (see section 1 for both URLs); a real deployment needs a properly issued certificate, not the dev cert. |
| Azure deployment: a Container App restarts in a loop (`ContainerBackOff`) and its console log ends in `Cannot find an authentication provider for 'ActiveDirectoryManagedIdentity'` | `Microsoft.Data.SqlClient` 7.x needs the separate `Microsoft.Data.SqlClient.Extensions.Azure` package for Entra ID login. Both must be referenced, at the same version, in the WebApp, API and Worker projects. The log shows only the stack tail in the portal Log stream; filter for it instead, e.g. `az containerapp logs show -g <rg> -n <app> --type console --tail 300 --format text \| grep -iE "login failed\|SqlException\|Exception:"`. |
| Azure deployment: `Login failed for user '<token-identified principal>'` in a Container App's log | The managed identity has no database user. Re-run the pipeline's Database stage; look for `Ensuring database user [id-atr-...] (db_owner)` in its log. |
| Azure deployment: Provision fails with `RequestDisallowedByAzure` / "region is currently not accepting new customers" | The subscription may not create resources in that region. Re-run with another `location` parameter (for example `polandcentral` or `northeurope`). |
| Azure deployment: the run fails immediately with "Environment atr-... could not be found" | Create the Azure DevOps Environment `atr-<resourceGroup>` (Pipelines > Environments, resource type None) before the first run; it is not reliably auto-created. |
| Azure deployment: Provision fails with `AuthorizationFailed` on creating the resource group or role assignments | The service connection's identity needs Owner (or Contributor plus User Access Administrator) on the subscription or the resource group. |
| Azure deployment: cannot read `atr-admin-password` in Key Vault although you are subscription Owner | The vault uses Azure RBAC for secrets; grant yourself **Key Vault Secrets User** on the vault first. |
