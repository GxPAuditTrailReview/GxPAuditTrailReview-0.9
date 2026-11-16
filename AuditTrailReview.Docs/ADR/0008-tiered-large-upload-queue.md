# 0008. Tiered large-upload processing through a database-backed job queue (`ScanUploadJob`)

Status: Accepted
Date: 2026-10-19 (decision taken 2026-09-18, thresholds revised 2026-10-02)

## Context
Manual web uploads were scanned inside the HTTP request that carried them. That is fine for a few thousand rows and breaks down for real audit trail exports: a 300,000-record file needs roughly nine minutes and a 5,000,000-record file (~714 MB) roughly twenty. A request that long times out, ties up a web worker, and is lost if the process restarts. The Integration Hub API had the same problem, and external callers had no way to ask whether a long upload had finished.

Constraints that shaped the answer:
- Every ingestion path must keep calling the one shared `ScanProcessingService.ProcessFileAsync` (ADR-0003), so the queue may only decide *where and when* a file is processed, never *how*.
- The existing FTP path (ADR-0005) ties every file to one fixed `Ftp:SystemId` per Worker Service instance, so it cannot carry an upload for an arbitrary system.
- The schema is managed by hand-written SQL scripts (ADR-0004), so a new table means a new numbered script.

## Decision
Uploads that cannot run synchronously are stored as rows in a new table, `dbo.ScanUploadJob` (script `ATR 0.23 20260918 - Add ScanUploadJob.sql`, entity `AuditTrailReview.Model.ScanUploadJob`), and processed by one of two tiers:

| Tier | Who creates it | Size | Where it runs | Bytes live in |
|---|---|---|---|---|
| (none) | WebApp | up to 10 MB | inline in the request | memory |
| `Tier.Immediate` | WebApp only | 10 MB to 50 MB | the WebApp process, via the in-memory `IBackgroundTaskQueue` | memory only |
| `Tier.Worker` | WebApp (above 50 MB) and **every** Integration Hub API upload | up to the 1 GB request limit | the Worker Service, which polls for `Queued` rows | `FileContent` column |

Rules that follow from the decision:
- **The API never processes a file.** It validates (400 for an empty file, a bad extension or an unknown system), stores a `Tier.Worker` row with `Source = Api` and answers `202 Accepted` with a `jobId`. Callers poll `GET api/ScanProcessing/scan/{jobId}`; the bundled Console client does this itself. A Worker Service must run wherever the API runs.
- **The Worker Service processes one job at a time**, oldest first (`ScanUploadJobPoller` calling `ScanUploadJobProcessor.ProcessNextPendingAsync`). A large file therefore delays the jobs behind it; they are queued, not stuck.
- **Claim before work.** A job moves `Queued` to `Processing` to `Completed` or `Failed`, modeled on `IngestedFile` (ADR-0005). `Tier.Worker` failures are requeued up to three attempts, then `Failed`. `FileContent` is cleared once consumed so the table never accumulates old blobs.
- **Restart recovery.** At startup the Worker Service resets any `Tier.Worker` row left in `Processing` back to `Queued`, and the WebApp marks stuck `Tier.Immediate` rows `Failed` (their bytes were only in memory). The Windows installer configures service auto-restart because recovery only runs at process start.
- **`FileContent` is never loaded through Entity Framework change tracking.** Status changes are set-based `ExecuteUpdateAsync` calls naming only the columns they touch, and the bytes are streamed with a `SequentialAccess` reader. Loading a ~700 MB blob through tracking took over 30 minutes and saturated the CPU. A tracked fallback exists only for the EF InMemory provider used by unit tests.
- **Completion feedback.** WebApp uploads record the uploader's user id; a bell icon in the layout polls `Pages/Notifications` every 30 seconds for finished, unacknowledged jobs. API uploads have no signed-in user and use the status endpoint instead.
- Stored `ErrorMessage` values include the inner exception, because Entity Framework's top-level message is a generic wrapper.

## Consequences
**Positive:**
- No request or connection is held open for the length of a scan, and a queued upload survives a restart of the API or the WebApp (for `Tier.Worker`).
- One processing path and one set of rule semantics for web, API and FTP uploads.
- A 5,000,000-record file was processed end to end in under 20 minutes (see `Test/TestStrategy.md`).

**Negative / known trade-offs:**
- A Worker Service is now mandatory next to the API; without it jobs stay `Queued` indefinitely. There is no health endpoint that says so (see `Technical Manual.md` section 6).
- Strictly sequential processing means one very large file delays everything behind it.
- `Tier.Immediate` is the weak tier: its bytes exist only in memory, so a WebApp restart loses the job.
- Peak memory still scales with the number of rows because parsing is not yet streaming (see `Technical Manual.md`, *Large-file processing*).
- Polling adds up to one poll interval of latency, and the bell is poll-based rather than pushed.
- The size thresholds are compile-time constants in `Scan.cshtml.cs`, chosen from measured timings; changing them needs a code change. `Model/ScanUploadJob.cs`'s class comment now quotes the current 1 GB ceiling (fixed; it previously quoted the original 500 MB figure). The `ATR 0.23` script itself still quotes the original 100 MB/500 MB figures and should be treated as superseded by this table.

## Alternatives considered
- **Keep synchronous processing and raise timeouts.** Rejected: it does not survive a restart and blocks a web worker for minutes.
- **Route large uploads through the FTP ingestion path.** Rejected: that path is bound to one `Ftp:SystemId` per Worker Service instance.
- **A message broker (RabbitMQ, Azure Service Bus).** Rejected for now: a new piece of infrastructure for a workload the shared database already handles, and it would not make the file bytes any easier to store.
- **Real-time notification (SignalR).** Rejected: jobs run minutes, so a 30-second poll is responsive enough without a push dependency.
