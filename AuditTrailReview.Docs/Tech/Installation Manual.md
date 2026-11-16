# Installation Manual v0.9 

## Purpose
This is a basic, step-by-step guide to get **GxPAuditTrailReview** (the `AuditTrailReview.WebApp`) running on a single machine for the first time: local development, a demo, or a first test install. It covers the web application only; Integration Hub (Worker Service / API) and Docker are covered briefly at the end as optional next steps. To choose between this manual, Docker and Azure, see [Deployment Options](Deployment%20Options.md).

---

## 1. Prerequisites

| Requirement | Notes |
|---|---|
| **.NET 10 SDK** | All projects target `net10.0`. If it's not already installed, use `scripts/install-dotnet.ps1` (Windows) or `scripts/install-dotnet.sh` (macOS/Linux) from the repo root; these install a user-local SDK with no admin rights needed. |
| **SQL Server** | LocalDB (bundled with Visual Studio) is enough for a first install. A full SQL Server instance works the same way; just adjust the connection string. |
| **Git** | To clone the repository. |
| **A code editor** | Visual Studio 2022+ (easiest, has a built-in "Run" button) or the `dotnet` CLI with any editor (VS Code, Rider, etc.). Visual Studio is not required. |
| **(Optional) An OpenAI API key** | Every manual file upload automatically calls OpenAI for a supplementary AI analysis. Without a valid key, **uploading a scan through the web UI will fail**, even though the file itself would parse and evaluate correctly. See step 5. |
| **(Optional) SMTP credentials** | Only needed if you want the "Send Credentials" (password reset email) feature to work. Everything else works without it. |

### WebApp upload size tiers

A manual upload through the WebApp UI is handled differently depending on its size:
- **Small (under 10 MB):** processed synchronously, inline in the request.
- **Medium (10 MB to 50 MB):** processed asynchronously in the WebApp process itself (`Tier.Immediate`). The bytes only live in memory, so a WebApp restart while one of these is still running marks it `Failed`.
- **Large (over 50 MB, up to the 750 MiB / 786,432,000-byte hard upload ceiling):** processed asynchronously by the Integration Hub **Worker Service** (`Tier.Worker`), one file at a time. The Worker Service must be running wherever files this size are uploaded, or they stay queued indefinitely.

### Hardware sizing for large audit trail files

There is no hard minimum for small files, but **scanning a very large file is memory-hungry**: the
Worker Service (or the WebApp, for a manual upload of up to 10 MB) reads the whole file, turns every
row into an in-memory record, and evaluates all rules over that list before saving. The figures
below combine what has been measured with rough estimates, so treat them as guidance, not limits.

| File size | Basis | Suggested machine for the process doing the scan |
|---|---|---|
| Up to ~300,000 records / ~42 MB | Measured (original performance series, `Test/TestStrategy.md` §2) | 8 GB RAM free for the scan process |
| ~5,000,000 records / ~714 MB | Measured once: ~19-22 min on an i7-13620H with **32 GB RAM** (SQL Server) | **32 GB RAM** and a fast SSD; 16 GB is likely not enough |

What drives memory (estimates from reading the code, not profiler measurements): the file content
is held as a .NET string (two bytes per character, so about twice the file size, with a higher peak
while it is built); every row then becomes a dictionary, is mapped to a second object, and the
findings are tracked by Entity Framework before the save. A manual upload also buffers the file in
the WebApp (roughly 2-3x the file size briefly) before storing it in `dbo.ScanUploadJob`. Only one
queued job is processed at a time, so memory use does not multiply with the queue length.

Practical guidance:
- **Use SQL Server (or the PostgreSQL flavor) on a separate machine from the Worker Service** for
  multi-million-record files, so the database server and the scan do not compete for the same RAM.
- **Docker Desktop on Windows/macOS limits the whole Docker VM** (WSL2 defaults to about half the
  machine's RAM). Running Postgres, the WebApp, the API and the Worker Service together and scanning
  a multi-million-record file can exhaust it and make Docker and the WebApp unresponsive. Raise the
  limit in `%UserProfile%\.wslconfig` (for example `memory=24GB` and `swap=16GB` under `[wsl2]`, then
  `wsl --shutdown`) or run fewer containers.
- Keep free disk space for the stored file (up to 750 MiB per queued upload, cleared once the job
  completes), the scan results, and the database transaction log.
- Files larger than 786,432,000 bytes (750 MiB) cannot be uploaded through the WebApp or API at
  all; use the FTP integration instead (§8).

---

## 2. Get the code

```bash
git clone <repository-url> GxPAuditTrail
cd GxPAuditTrail
```

---

## 3. Create the database

The application does **not** create or migrate its own database schema at startup: the schema must exist before you run the app. It's provided as a ready-to-run SQL script.

1. Open **SQL Server Management Studio** (or `sqlcmd`) and connect to your SQL Server / LocalDB instance.
2. You don't need to create the database by hand: the script below begins with `CREATE DATABASE [GxPAuditTrail]`, so running it against your server creates the database too. (If you'd rather create it yourself first, use that same name, `GxPAuditTrail` and not `AuditTrailReview`, to match the connection string in step 4.)
3. Run the latest full schema script:
   `AuditTrailReview.Database/Database schema/ATR 0.8 20261020-SQL2025.sql`
   (an `-SQL2012` variant of the same script exists in the same folder for older SQL Server
   compatibility targets -- use it instead if your instance needs it; the two are otherwise
   equivalent). This is the newest full "create from scratch" snapshot in that folder: `ATR 0.2`
   through `ATR 0.7 20260910` are earlier snapshots kept for history, superseded by this one. It
   includes the ASP.NET Identity tables, the application's own core tables (Scan, ScanResult,
   SystemATR, Rules, EngineSettings, logs, etc.), `SecuritySettings`, `EmailSettings`, `AiSettings`,
   `IngestedFile`, `DocChunk`/`DocIndexStatus` (dropped again by `ATR 0.34` below), `ScanUploadJob`, and
   `ScanResult.HasFinding` with its filtered index.
4. **Then run every incremental script numbered `0.20` and above, in version order** (sort by the
   version number, not the file name: `0.7` sorts after `0.20` alphabetically; see ADR-0004). They
   are small `ALTER`/`CREATE` scripts layered on the `ATR 0.8` snapshot. As of v1.0 they are:

| Script | Change |
|---|---|
| `ATR 0.20` | `EngineSettings`: drop `ReasonMinAlphabeticChars` |
| `ATR 0.21` | Add `ScanFile` table |
| `ATR 0.22` | Add `ScanResult.ScanID` index |
| `ATR 0.23` | Add `GeneralSettings` table |
| `ATR 0.24` | `AiSettings`: add `MaxFindingsPerAnalysis` |
| `ATR 0.25`-`0.29` | `AuditTrail`: add `SystemID`, `ScanID`, `SystemTypeID`, `TargetUserId`, `TargetRoleId` columns |
| `ATR 0.30` | `Scan`: add `UserId` (uploader) |
| `ATR 0.31` | `AiSettings`: add `ApiKeyEncrypted` and `Model` |
| `ATR 0.32` | `EmailSettings`: add `UserName` and `PasswordEncrypted` |
| `ATR 0.33` | `AiSettings`: add provider, endpoint and API-version columns |
| `ATR 0.34` | Drop `DocChunk`, `DocIndexStatus` and `AiSettings.EmbeddingDeploymentName` (documentation chatbot removed) |
| `ATR 0.35` | `AiSettings`: drop `DeploymentName` and `ApiVersion` |
| `ATR 0.36` | Add `ApiKeyCredential` table (Integration Hub API keys) |
| `ATR 0.37` | Add `McpApiKeyCredential` table |
| `ATR 0.38` | `SecuritySettings`: add session timeout columns |
| `ATR 0.39` | `McpApiKeyCredential`: add `UserId` |
| `ATR 0.40` | Add `FtpConfiguration` table (FTP settings moved from appsettings to the database) |

   Check the folder for anything newer than `0.40` in case a later script was added after this
   manual was written. The WebApp never runs any of these for you: it does not call `Migrate()` or
   `EnsureCreated()` against SQL Server. Skipping one is not safe: the application will fail at
   runtime on the first query that touches a missing column or table.

> Once the tables exist, the application fills them with starter data automatically the first time it runs (see step 6); you do not need to insert any rows yourself.

---

## 4. Point the app at your database

Open `AuditTrailReview.WebApp/appsettings.json` (or, for local development, `appsettings.Development.json`, where values override `appsettings.json`) and set the connection string to match your SQL Server instance:

```json
"ConnectionStrings": {
  "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=GxPAuditTrail;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True"
}
```

The default value works out of the box with LocalDB and the database name from step 3. For a full SQL Server instance, replace `Server=` with your instance name and adjust authentication as needed.

---

## 5. Set the initial admin account and (optional) API keys

Still in `appsettings.json` / `appsettings.Development.json`:

```json
"SeedAccount": {
  "Email": "admin@yourcompany.com",
  "Password": "Choose-A-Strong-Password1!"
},
"OpenAI": {
  "ApiKey": "sk-...your-key-here..."
},
"Security": {
  "EncryptionKey": "...base64, 32 random bytes..."
},
"SmtpSettings": {
  "Host": "", "Port": "587", "EnableSsl": "true",
  "SenderEmail": "", "SenderName": "", "UserName": "", "Password": ""
}
```

- **SeedAccount**: the email/password here become your first login, automatically created with the **Admin** role the first time the app starts against an empty user table. If you leave `Password` blank, no seed account is created and you'll need another way to get your first user in.
- **OpenAI**: required for the "Run a manual scan" workflow to succeed (see the note in §1). If you don't have a key yet, you can still install and log in; just don't upload a scan through the web UI until it's set. This config value is only a fallback -- once the app is running, an Admin can instead set (and later change) the OpenAI API key from **Admin Panel → Application Settings → AI Analysis**, where it's stored encrypted in the database rather than in a config file.
- **Security:EncryptionKey**: the master key used to encrypt/decrypt both the OpenAI API key (saved from the AI Analysis tab above; never needed if you only ever set the key via `OpenAI:ApiKey`) and the SMTP password (saved from the Email tab; never needed if you only ever set it via `SmtpSettings:Password`). Generate one once with `[Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))` in PowerShell, and set the *same* value on every machine that runs the WebApp or the Integration Hub Worker Service -- whichever process saved a secret, only a process with this same key can read it back.
- **SmtpSettings**: only needed for the admin "Send Credentials" email feature; safe to leave blank otherwise.

> ⚠️ Do not commit real secrets into `appsettings.Development.json` if it's tracked in your git repository; check first with `git status`/`git ls-files`.

---

## 6. Build and run

**Using the `dotnet` CLI:**
```bash
cd AuditTrailReview.WebApp
dotnet restore
dotnet build
dotnet run
```

**Using Visual Studio:** open the solution, set `AuditTrailReview.WebApp` as the startup project, and press F5 (or Ctrl+F5 to run without debugging).

On first startup the app automatically seeds:
- the three roles (Admin, Super User, User),
- your admin account from `SeedAccount` (if configured),
- the 19 rule-engine rules (all enabled by default) and their 5 categories,
- 8 default system types (LIMS, MES, CDS, ERP, QMS, SCADA, DMS, ELN),
- default engine settings (reason length, working hours, etc.),
- a default security (password/lockout) policy, editable from **Config Panel → Application
  Settings** (Admin only),
- default email (SMTP host/port/SSL/sender) settings copied from `appsettings.json`'s
  `SmtpSettings` section, and default AI analysis settings (temperature/timeout/etc.) --
  both editable from the same Application Settings page (Admin or Super User).

---

## 7. Log in and verify

1. Open the URL shown in the console (by default `https://localhost:7190` or `http://localhost:5059`; check `AuditTrailReview.WebApp/Properties/launchSettings.json` if it's different on your machine).
2. Sign in with the `SeedAccount` email/password from step 5.
3. You should land on the Dashboard. From there:
   - Create a system (**Systems → Create**).
   - Upload a sample `.csv`/`.xml`/`.json` audit trail file from that system's page to confirm parsing, rule evaluation, and (if you configured an OpenAI key) AI analysis all work end-to-end.
   - Check **AdminPanel → Users/Roles** to create additional accounts as needed.

If something doesn't work, see Troubleshooting below.

---

## 8. Optional: Integration Hub (Worker Service and API)

These are separate, optional components for automated/scripted file ingestion, not required to use the web application. If you deploy the API, deploy the Worker Service with it.

- **`AuditTrailReview.IntegrationHub.API`**: an HTTP API for external systems. Configure `ConnectionStrings:DefaultConnection` (same database) and `Security:EncryptionKey` (base64, 32 bytes -- must be byte-identical to the WebApp's own value) in its `appsettings.json`, then `dotnet run` from that project folder. Callers must send a valid API key in an `X-Api-Key` header; keys are generated and revoked from the WebApp's AdminPanel -> Settings -> API tab (stored encrypted in `dbo.ApiKeyCredential`), not configured in `appsettings.json` -- no key works out of the box, so sign in to the WebApp as an admin and generate one before testing the API. Uploads are capped at 786,432,000 bytes (750 MiB) server-side; a caller sending a larger file should use the FTP integration above instead. The API only validates and stores an upload (HTTP 202 with a `jobId`); **the Worker Service below must be running** to process it, so deploy the two together. Callers poll `GET /api/ScanProcessing/scan/{jobId}` for the result; the bundled `AuditTrailReview.Console` client does this itself.
- **Worker Service (`AuditTrailReview.IntegrationHub.WorkerService`)**: polls an FTP location on a schedule and ingests new files automatically. Configure `ConnectionStrings:DefaultConnection` and `Security:EncryptionKey` (the same key as the web app) in its `appsettings.json`, then `dotnet run` from that project folder. **FTP settings are no longer in `appsettings.json`.** They are kept in the database (`dbo.FtpConfiguration`, script `ATR 0.40`) and edited in the web app under **Admin Panel > Application Settings > FTP**: a master switch (**off by default**, so a fresh or upgraded install does not poll any FTP server until an admin turns it on), host, port, transport security (None = plain FTP, Explicit FTPS, Implicit FTPS), server-certificate validation with an optional pinned thumbprint, username, password (stored encrypted), target system, remote folder, `ProcessedFolder`/`FailedFolder` (covered below) and the polling/stale-file intervals. Changes apply on the Worker's next poll without a restart. Plain FTP sends credentials and files in clear text; use FTPS wherever the server supports it. SFTP (SSH) is not supported. **Upgrading an install that used the old `Ftp` section:** re-enter those values in the FTP tab and switch it on. **On Windows**, it can instead be installed as a proper Windows Service (survives reboots/logoff) via the installer built from `installer/WorkerService/` (see that folder's `README.md` for the build steps); the installer copies the same `appsettings.json` you'd otherwise hand-edit here, so the configuration above still applies after installing. **As of 2026-09-18**, the installer also configures the service to auto-restart itself (1 minute after the 1st failure, 5 minutes after each one after that) if the process ever stops unexpectedly, so an outage doesn't leave scans sitting unprocessed until someone notices and restarts it by hand: the Worker Service's own startup recovery (`ScanUploadJobProcessor.RecoverStuckWorkerJobsAsync`/`FtpFileCheckerService.RecoverStuckClaimsAsync`) then picks the backlog back up automatically. If you register the service by hand instead of using the installer, replicate this with `sc create AuditTrailReviewWorkerService binPath= "<path>\AuditTrailReview.IntegrationHub.WorkerService.exe" start= auto`, then `sc failure AuditTrailReviewWorkerService reset= 86400 actions= restart/60000/restart/300000/restart/300000` and `sc failureflag AuditTrailReviewWorkerService 1`. **As of 2026-09-15, the FTP account needs write, rename, and create-directory permission in `RemoteFolder`, not just read/download.** After a file is processed, the Worker Service moves it into a `ProcessedFolder` subfolder (default `Processed`) on success, or a `FailedFolder` subfolder (default `Failed`) once it has been retried 3 times and given up on, creating either subfolder automatically the first time it's needed. This keeps `RemoteFolder` itself showing only files still awaiting processing, and gives an admin/IT a place to review and delete already-handled files. Set either setting to an empty string to disable that move and leave files where they were found (the previous behavior); useful if the FTP account genuinely can't be granted write access. A failure to move a file (e.g. insufficient permission) is logged as a warning only; it does not affect the scan or the file's `IngestedFile` status either way. **As of 2026-09-15**, a throttled background sweep (the stale-file check interval, default 60 minutes between checks) also retries the move for any file that's still sitting in `RemoteFolder` more than the stale-unmoved threshold (default 60 minutes) after it should have been moved, covering the case where the original move attempt itself failed and would otherwise never be retried.

Both share the same database as the web app, so anything they ingest shows up immediately in the web UI.

### Optional: running in Docker

`AuditTrailReview.WebApp`, `AuditTrailReview.IntegrationHub.API`, and `AuditTrailReview.IntegrationHub.WorkerService` (Worker Service) each ship a `Dockerfile`. Build and run any of them the standard way, e.g.:

The console client (`AuditTrailReview.Console`) does not ship a `Dockerfile`; run it directly with `dotnet run` from its project folder, or `dotnet publish` it and run the resulting binary.
```bash
docker build -f AuditTrailReview.WebApp/Dockerfile -t audittrailreview-webapp .
docker run -p 8080:8080 -p 8081:8081 audittrailreview-webapp
```
Supply the same configuration (connection string, seed account, API keys) via environment variables or a mounted `appsettings.json`.

**Try-it-out flavors without SQL Server.** Two compose files in the repository root run the WebApp
against a database that needs no separate server, creating the schema from the EF Core model at startup
(`Database:Provider` = `Sqlite` or `Postgres`, set by `appsettings.Sqlite.json` / `appsettings.Postgres.json`
and `ASPNETCORE_ENVIRONMENT`):

```bash
docker compose --project-directory . -f docker/docker-compose.sqlite.yml up --build     # WebApp + embedded Sqlite file, http://localhost:8080
docker compose --project-directory . -f docker/docker-compose.postgres.yml up --build   # Postgres + WebApp (8080) + Integration Hub API (8081) + Worker Service
```

- Sign in with the `SeedAccount__*` values in the compose file and change them (and the database password) before
  using them for anything beyond local testing.
- The SQLite flavor has no Worker Service, so uploads large enough to need the Worker Service queue stay `Queued`.
  The PostgreSQL flavor includes the Worker Service and the API; its FTP check only does something once you point
  FTP at a reachable server and a system in the web app (Settings > FTP).
- **As of v0.9**, `docker/docker-compose.postgres.yml` also starts a `backup` service that runs a nightly `pg_dump`
  (03:00, `TZ` configurable) into `./backups/daily`, gzipped, keeping 14 days by default (`BACKUP_KEEP_DAYS`).
  Copy `./backups` off the host, since a backup on the same disk doesn't survive losing that disk, and replace the
  `POSTGRES_PASSWORD` placeholder (used identically by the `postgres` and `backup` services) before deploying
  this flavor beyond local trials. The compose file's own comments document how to restore a dump or run one
  on demand.
- These flavors are for evaluation and functional testing, not for real GxP use and not as a performance benchmark.
  The supported deployment remains SQL Server (ADR-0002, ADR-0004), which is the only one the SQL scripts above apply to.
- For a step-by-step walkthrough of running these compose flavors on a remote Linux VPS over SSH, see
  `AuditTrailReview.Docs/CICD/Run docker compose on VPS.md`.

**Memory:** containers share the Docker VM's memory limit; see *Hardware sizing for large audit trail files* in §1 before scanning a multi-million-record file in Docker.

---

## 9. Troubleshooting

| Symptom | Likely cause / fix |
|---|---|
| App won't start / DB connection errors | Confirm the database from step 3 exists and the connection string in step 4 matches your SQL Server instance name. |
| Can't log in with the seed account | Check that `SeedAccount:Password` was set *before* the app's first run against an empty database: the seed only runs once, when no users exist yet. If you already ran the app once without a password, create a user manually via SQL or restart against a fresh database with the password set. |
| Uploading a scan fails with an error | Most likely a missing/invalid `OpenAI:ApiKey`, since every manual upload calls OpenAI automatically after rule evaluation. Set a valid key, or ingest the file via the Integration Hub API/Worker Service instead, which skip the AI step entirely. |
| "Send Credentials" email doesn't send | `SmtpSettings` is not configured. Fill in `Host`, `Port`, `SenderEmail`, `UserName`, `Password`. |
| Port already in use | Change the port in `AuditTrailReview.WebApp/Properties/launchSettings.json`, or stop whatever else is using it. |
