# Audit Trail Rule Engine -- How It Works

**Purpose:** a code-and-database-grounded reference for how GxPAuditTrailReview actually evaluates an uploaded audit trail today -- what each of the 19 active rules checks, how the AI-assisted analysis step works, and how a file's size determines which code path processes it. This is a description of current behavior, not a proposal document (see `AuditTrailReview.Docs/Reviews/Audit-Trail-Rule-Regulatory-Basis-Review.md` for the regulatory-sourcing history and rule-management decisions that produced today's set).

---

## 1. Where this lives and how a file becomes findings

Every ingestion path -- manual WebApp upload, the Integration Hub Worker Service (FTP polling and the `ScanUploadJob` queue), and the Integration Hub API -- converges on one method: `AuditTrailReview.Engine.ScanProcessingService.ProcessFileAsync` (`AuditTrailReview.Engine/ScanProcessingService.cs:57`). The manual-upload handler for files under 10 MB (`Pages/Scan/Scan.cshtml.cs`) inlines the same steps itself rather than calling `ProcessFileAsync`, but runs the identical sequence against the identical shared engine (ADR-0003).

The sequence for any file:

1. **Parse** -- `AuditTrailFileParser.ParseContent` turns the raw CSV/XML/JSON into generic rows (`.csv`, `.xml`, `.json` are the only supported extensions; `ScanProcessingService.GetUnsupportedFileTypeMessage`, line 43, rejects anything else before any parsing happens).
2. **Map** -- `AuditTrailMapper.MapRowsToAuditTrailInfo` matches each source column header against keyword lists (`AuditTrailMapper.cs`, e.g. `UserIDKeywords`, `OperationKeywords`, `DateTimeKeywords`, `UniqueIdentifierKeywords`) to populate a common `AuditTrailInfo` row: `UniqueIdentifier`, `Timestamp`/`TimestampRaw`, `UserID`, `Operation`, `ObjectType`, `Object`, `Field`, `OldValue`, `NewValue`, `Reason`, `Source` (`AuditTrailReview.Model/AuditTrailInfo.cs:10-39`). This header-heuristic mapping step is *not* shared code between the manual-upload parser and `ScanProcessingService`'s parser -- both call into the same `AuditTrailMapper`, but ADR-0003 flags this as a place the two paths could in principle drift.
3. **Evaluate** -- `ScanResultService.ApplyRulesAndGenerateResults` (`AuditTrailReview.Engine/ScanResultService.cs:30`) runs the 19 rules described below against every mapped row and returns one `ScanResultInfo` per input row, each carrying a semicolon-joined `Finding` string (e.g. `"[B1] ...; [C3] ..."` when more than one rule fires on the same row) and a `HasFinding` bit.
4. **Persist** -- the `Scan` row and all of its `ScanResult` rows are saved inside one explicit transaction (`ScanProcessingService.cs:128-167`), so a crash mid-evaluation leaves nothing behind rather than a scan with zero results.
5. **Queue AI analysis** -- after that transaction commits, a supplementary AI-analysis step is queued in the background (never blocking, never able to fail the scan) -- see §3.

Rule enablement and every configurable threshold are read fresh from the database at the start of each scan (`dbo.Rules.IsEnabled` and the singleton `dbo.EngineSettings` row), so behavior is driven entirely by stored configuration, identically regardless of which ingestion path triggered the scan.

---

## 2. Configuration model

Two database tables drive the engine:

- **`dbo.RuleCategories`** (`Id`, `Code`, `Name`) -- the five categories A–E, seeded once at WebApp startup if empty (`Program.cs:275-338`).
- **`dbo.Rules`** (`Id`, `RuleCode`, `Description`, `IsEnabled`, `RuleCategoryId`, `RegulatoryBasis` `nvarchar(200)`, `GuidanceBasis` `nvarchar(300)`) -- one row per rule. `Id` is the stable `RulesEnum` value from `AuditTrailReview.Model/Rule.cs` and is **never reused** once a rule is retired -- see §7. `RegulatoryBasis` is populated only when the rule is essentially a restatement of a specific binding clause (21 CFR Part 11/58/211/312/812, EU Annex 11); `GuidanceBasis` is populated instead for a rule that is a risk-based/heuristic control backed only by harmonization guidance (PIC/S PI 041-1, WHO TRS 996 Annex 5, ICH E6(R3), OECD No. 17) -- the two columns are mutually exclusive by design (`Rule.cs:14-46`).
- **`dbo.EngineSettings`** -- a singleton row (`Id = 1`) holding every configurable threshold, editable from `ConfigPanel`. Current columns (`AuditTrailReview.Model/EngineSettings.cs`, matching `ATR 0.8 20261020-SQL2025.sql:337-354`):

| Column | Default | Used by |
|---|---|---|
| `WorkdayStartHour` / `WorkdayEndHour` | 7 / 18 | C3 |
| `AllowWeekends` | `false` | C3 |
| `WorkdayTimeZone` | `NULL` (→ UTC) | C2, C3 |
| `UsernameMinLength` | 3 | D2 |
| `AdditionalSystemAccountPatterns` | `NULL` | D1 |
| `MassChangeThresholdCount` | 10 | E1 |
| `MassChangeWindowSeconds` | 1.0 | E1 |
| `AiAnalysisEnabled` | `true` | AI analysis (§4) |

One stray column, `ReasonMinAlphabeticChars` (`DF_EngineSettings_ReasonMinAlphabeticChars` default `1`), existed in the live schema but backed no current rule -- it belonged to the retired A5 ("reason must contain alphabetic characters") rule and was apparently missed when that rule's other now-removed settings columns (`AdditionalPersonalNames`, `BoilerplateReasonMinRowCount`/`ThresholdPercent`, `TimeGapThresholdDays`) were dropped. Dropped by `ATR 0.20 20261026 - EngineSettings Drop ReasonMinAlphabeticChars.sql` (see §6).

---

## 3. The 19 rules

Every rule's `ApplyRulesAndGenerateResults` enable flag is independent and read per scan; a disabled rule neither runs nor contributes to `Finding`. All string comparisons the rules themselves perform are case-insensitive (`StringComparison.OrdinalIgnoreCase`) and values are typically compared trimmed.

### Category A -- Data Integrity

| Code | DB Id | Description | Regulatory/Guidance basis |
|---|---|---|---|
| A1 | 5 | Mandatory fields: user, timestamp, action | 21 CFR 11.10(e); 58.130(e); 211.194(a)(7); EU Annex 11 §12.4 |
| A2 | 24 | Record-chain continuity | 21 CFR 11.10(e); 58.130(e); 211.68(b) |
| A3 | 7 | No duplicate records | WHO TRS 996 Annex 5 App. 1 (Accurate); OECD No. 17 §3.2; ICH E6(R3) §2.12.2 |

- **A1 -- mandatory fields** (`ScanResultService.cs:385-400`): flags a row if `Operation`, `Timestamp`, or `UserID` is missing/blank. `before`/`after`/`reason` are deliberately *not* checked here -- those are enforced conditionally per action type by B2–B6 instead (a `Create` legitimately has no "before" value, so a blanket requirement would conflict with B4). Finding: `"[A1] Missing mandatory fields: {list}"`.
- **A2 -- record-chain continuity** (precomputed `ScanResultService.cs:133-174`, applied `559-566`): for each `ObjectType`+`Object`+`Field` (`GetFieldHistoryKey`, finer-grained than A3/C1's object-level key, since two fields on the same object carry independent value histories), rows are walked in actual chronological `Timestamp` order within the scan. An `Update`'s `OldValue` must equal the immediately preceding row's `NewValue` for that same object/field; a mismatch is flagged on the row that broke the chain. A `Delete` resets the tracked value (nothing to compare against afterward). Rows missing `Object` or `Timestamp` are excluded entirely. Finding: `"[A2] Record-chain continuity broken -- OldValue '{actual}' does not match the previous record's NewValue '{expected}' for this object/field"`.
- **A3 -- no duplicate records** (precomputed `74-95`, applied `402-413`): groups rows by `ObjectType`+`Object`+`Field`+`UserID`+`Timestamp`+`OldValue`+`NewValue`+`Operation`; any group with more than one row flags every row in it. The object/field identity is deliberately part of the key (a fix for a reported bug) -- without it, several different records legitimately changed the same way by the same user in the same instant (a bulk operation) would misfire as "duplicates" of each other; that scenario is E1's concern, not A3's. Finding: `"[A3] Duplicate record detected -- same record ({objectType} '{object}', field '{field}'), UserName: ..., DataTime: ..., Operation: ..., OldValue: ..., NewValue: ..."`.

### Category B -- Action Consistency

| Code | DB Id | Description | Basis |
|---|---|---|---|
| B1 | 3 | Modify must change the value (before ≠ after) | 21 CFR 11.10(e) |
| B2 | 9 | Update must have both before and after values | 21 CFR 11.10(e); 58.130(e) |
| B3 | 10 | Create must have the after value | 21 CFR 11.10(e) |
| B4 | 11 | Create must not have the old value | 21 CFR 11.10(e) |
| B5 | 13 | Update must have a reason for update | 58.130(e); EU Annex 11 §9 |
| B6 | 14 | Delete must have a reason for deletion | 58.130(e); EU Annex 11 §9 |
| B7 | 15 | Delete must retain the old value | 21 CFR 11.10(e); 58.130(e) |
| B8 | 27 | Approver/reviewer ≠ creator/modifier (segregation of duties) | 21 CFR 211.194(a)(8) |

All of B1–B7 are simple, single-row checks (`ScanResultService.cs:347-470`), keyed off `Operation` matching exactly `"Update"`/`"Create"`/`"Delete"`:
- **B1**: `Operation = Update` and `OldValue == NewValue` → `"[B1] Update operation did not change the value -- before and after are identical: '{value}'"`.
- **B2**: `Operation = Update` and either `OldValue` or `NewValue` is empty → `"[B2] Update operation is missing required value fields: {list}"`.
- **B3**: `Operation = Create` and `NewValue` is empty → `"[B3] Create operation is missing the new value (NewValue is empty)"`.
- **B4**: `Operation = Create` and `OldValue` is non-empty and not literally `"null"`/`"(null)"` (some source systems use those as placeholders) → `"[B4] Create operation must not have an old value -- OldValue: '{value}'"`.
- **B5**: `Operation = Update` and `Reason` is blank → `"[B5] Update operation is missing a reason for update (Reason is empty)"`.
- **B6**: `Operation = Delete` and `Reason` is blank → `"[B6] Delete operation is missing a reason for deletion (Reason is empty)"`.
- **B7**: `Operation = Delete` and `OldValue` is empty → `"[B7] Delete operation must retain the old value - OldValue is empty"`.

- **B8 -- segregation of duties** (precomputed `176-228`, applied `568-573`): for each `ObjectType`+`Object` (`GetRecordKey`), collects the set of users who did a `Create` or `Update` on it anywhere in the scan, then flags any `Approve`/`Review` row on that same object performed by one of those same users -- the conflicting edit does not need to be adjacent, since segregation of duties is a standing requirement, not a one-time adjacency check. Recognizes exactly the literals `Create`/`Update`/`Approve`/`Review` (no fuzzy synonyms like "Approved" or "Sign-off"); rows missing `Object` or `UserID` are skipped. Finding: `"[B8] User '{user}' both created/modified and approved/reviewed the same object -- segregation of duties violation"`.

### Category C -- Temporal Checks

| Code | DB Id | Description | Basis |
|---|---|---|---|
| C1 | 4 | Timestamps in logical order for each record | 58.130(e); 211.100(b) |
| C2 | 16 | No timestamps in the future | 58.130(e); 211.100(b) |
| C3 | 17 | Deviations from normal working hours | PIC/S PI 041-1 §5.6.2 / §9.8 |

- **C1 -- chronological order per record** (direction auto-detected per record `304-339`, checked per row `357-382`): rows are grouped by `ObjectType`+`Object` (`GetRecordKey`; rows with neither populated all collapse into one shared group, reproducing whole-file behavior for data without object identity). For each group, the expected direction (ascending/descending) is auto-detected from up to the first 10 consecutive timestamp pairs, then every subsequent row in that record is checked against the previous one in that same direction. Finding: `"[C1] Timestamp out of chronological order (detected order: {direction}) -- record: ..., previous: ..."`.
- **C2 -- no future timestamps** (`472-491`): the row's timestamp is resolved to a true UTC instant via `ResolveTimestampToUtc` (`695-707`) -- `Utc`-kind values pass through; `Local`-kind values (produced only when the source string carried an explicit numeric offset) are inverted with `ToUniversalTime()`; `Unspecified`-kind values (the common case -- no timezone info in the source) are assumed to already be local plant time in the configured `WorkdayTimeZone` and converted from there, rather than naively assumed to be UTC. The result is compared against `DateTime.UtcNow`. Finding: `"[C2] Timestamp is in the future -- value: ..., system time (UTC): ..."` (with a parenthetical kind/conversion note when a real conversion happened).
- **C3 -- working-hours deviation** (`493-526`): the same UTC-resolved instant is converted into the configured `WorkdayTimeZone` (`ConvertToWorkdayTimeZone`, `789-793`) and its `Hour`/`DayOfWeek` are checked against `WorkdayStartHour`/`WorkdayEndHour`/`AllowWeekends`. `WorkdayTimeZone` is a nullable IANA ID (e.g. `"Europe/Warsaw"`); a null/empty/unresolvable value falls back to UTC (`ResolveWorkdayTimeZone`, `760-777`) rather than failing the scan. Finding: `"[C3] Change recorded outside normal working hours -- value: ..., reason: weekend ({day} {zone})"` or `"...outside working hours ({start}:00–{end}:00 {zone})"`.

C2 and C3 deliberately share the same ambiguous-timestamp assumption (`ResolveTimestampToUtc`) so they never silently disagree about the same row -- this is the documented successor to a removed rule, A3 ("timestamp must be UTC"), whose underlying concern this logic still carries.

### Category D -- User Validation

| Code | DB Id | Description | Basis |
|---|---|---|---|
| D1 | 22 | Username must not be a shared/generic/system account | 21 CFR 11.10(d)/(g); 58.130(e); 211.68(b); EU Annex 11 §12.1 |
| D2 | 20 | Username must contain at least X characters | PIC/S PI 041-1 §9.5; WHO TRS 996 Annex 5 App. 1 (Attributable) |

- **D2 -- minimum username length** (`529-533`): `UserID.Length < EngineSettings.UsernameMinLength` (default 3). Finding: `"[D2] Username is too short -- value: '{value}' ({n} character(s), minimum {min} required)"`.
- **D1 -- shared/generic/system account** (`535-540`, `SystemAccountHeuristics.IsLikelySystemAccount`, `AuditTrailReview.Engine/SystemAccountHeuristics.cs:56-95`): flags a login that matches (1) a built-in exact-match list (`admin`, `administrator`, `system`, `sa`, `root`, `service`, `support`, `operator`, `guest`, `anonymous`, `unknown`, `default`, `test`, `demo`, `sample`, `null`, `none`, `n/a`, `na`, `superuser`, `poweruser`, `help`, `info`); (2) a built-in naming-convention prefix (`svc`, `sys`, `system`, `batch`, `auto`, `bot`, `api`, `job`, `cron`, `daemon`) -- but only when followed by a separator (`-`/`_`/`.`) or a digit, so ordinary words merely starting with the same letters (`"Testimony"`, `"systema"`) aren't caught; or (3) an admin-configured pattern in `EngineSettings.AdditionalSystemAccountPatterns` (comma/newline-separated, a trailing `*` matches as a prefix with no separator/digit guard). D1 is deliberately independent of (not the logical negation of) any "looks like a personal name" heuristic -- most real individual logins (`"jsmith"`) match neither signal. Finding: `"[D1] Username '{login}' appears to be a shared/generic/system account, not an individual login"`.

### Category E -- Anomaly Detection

| Code | DB Id | Description | Basis |
|---|---|---|---|
| E1 | 21 | Mass changes in a short time | PIC/S PI 041-1 §5.6.2 / §9.8 |
| E2 | 28 | Oscillating value pattern (A→B→A→B) | PIC/S PI 041-1 §5.6.2 / §9.8 |
| E3 | 29 | Nullifying data (genuine value → blank/"0") | PIC/S PI 041-1 §5.6.2 / §9.8 |

- **E1 -- mass-change burst** (precomputed `97-131`, applied `542-557`): rows are grouped **by `UserID` first** (a fix for a reported bug -- a burst is meant to catch one user/script editing abnormally fast, not several unrelated users each making one change in the same window), then within each user's timestamp-ordered rows a sliding window finds any run of `MassChangeThresholdCount` (default 10) or more rows whose timestamps all fall within `MassChangeWindowSeconds` (default 1.0) of each other. Every row in a qualifying burst is flagged. The finding's displayed timestamp only shows milliseconds when the source data actually declared sub-second precision (`HasSubSecondPrecision`, `723-733`), so a plain `HH:mm:ss` source never prints a fabricated `.000`. Finding: `"[E1] Mass change detected -- record is part of a burst of {n} or more modifications by the same user ('{user}') within {window} second(s) at {timestamp}"`.
- **E2 -- oscillating value pattern** (precomputed `230-299`, applied `575-582`): for each `ObjectType`+`Object`+`Field` (same key as A2), walks that field's `Create`/`Update` values in chronological order and looks for four consecutive values `v0, v1, v2, v3` where `v0 == v2`, `v1 == v3`, and `v0 != v1` -- a strict A→B→A→B alternation. The row carrying the fourth value is flagged; a longer run flags again at every point it continues. A `Delete` resets the tracked sequence. Finding: `"[E2] Oscillating value pattern detected -- value alternates between '{A}' and '{B}' for this object/field"`.
- **E3 -- nullifying data** (`584-605`, a plain single-row check -- no precomputation): fires on an `Update` where `OldValue` was genuine (non-blank, not already `"0"`) and `NewValue` is blank or exactly `"0"` after trimming. No other numeric-zero spelling (`"0.0"`, `"-0"`, …) is recognized -- a deliberate scope limit to keep the check predictable rather than attempting general numeric parsing. Deliberately overlaps with B2 on the "Update to blank" case (B2's concern is completeness; E3's is a risk-based "a real value just got wiped" signal) -- both findings can legitimately co-occur on the same row. Finding: `"[E3] Value nullified -- changed from '{old}' to blank"` or `"...to '0'"`.

---

## 4. AI-assisted analysis

Separate from the 19 deterministic rules above, every scan optionally gets a supplementary, narrative AI analysis pass (ADR-0006).

- **Trigger and scope:** queued inside `ScanProcessingService.ProcessFileAsync` (`ScanProcessingService.cs:179-245`), *after* the rule-based `Scan`/`ScanResult` rows have already committed in their own transaction -- so AI analysis can never affect whether the rule-based scan itself succeeds. Gated by a single flag, `EngineSettings.AiAnalysisEnabled` (default `true`). Because `ProcessFileAsync` is the one shared method behind every ingestion path, **this runs for manual WebApp uploads, FTP-ingested files, and both `ScanUploadJob` tiers alike** -- all four paths are symmetric today. (`Pages/Scan/Scan.cshtml.cs` carries its own near-identical inline copy of the same queuing block for the small, under-10 MB synchronous path, since that path doesn't call `ProcessFileAsync` for rule evaluation either.)

  (`EngineSettings.AiAnalysisEnabled`'s code comment and FR-035 used to describe this as "manual-upload only" and claim the Worker Service/API paths never queue AI analysis; both have been corrected to match the code, which was extended to every ingestion path by a later product decision recorded in ADR-0006.)

- **What gets sent:** the full set of just-computed rule-based results for the scan, serialized as camelCase JSON (`ScanResultService.ConvertScanResultsToJson`).
- **Model and prompt:** `AuditTrailReview.Engine/AI/OpenAIService` calls OpenAI's Chat Completions API using a hardcoded model, `gpt-4o-mini` -- deliberately not admin-configurable, since the prompt/parsing pair is tuned against this model's output shape. `AuditTrailPromptBuilder.Build()` supplies a fixed GxP/21 CFR Part 11/EU Annex 11-framed instruction asking the model to find *cross-record* patterns (not simply restate rule findings) across four categories -- Compliance, Behavioral Anomalies, Data Anomalies, Sequence/Workflow Anomalies -- and to return a raw JSON object with exactly two plain-text fields, `insights` and `recommendations`.
- **Configurable per-call behavior:** a singleton `dbo.AiSettings` row (`Temperature`, `MaxOutputTokens`, `RequestTimeoutSeconds` default 60, `SystemMessage`) is read fresh for every call and is editable from `AdminPanel/Settings`' AI Analysis tab; the model and prompt template itself are shown there read-only, not editable. The API key (`OpenAI:ApiKey` / `OpenAI__ApiKey` env var) lives only in app configuration, never in the database.
- **Response handling:** `ScanResultService.ParseOpenAIResponse` extracts the embedded JSON from the chat response (with `DateParseHandling.None` so an ISO-8601-looking string inside a field is never silently reinterpreted as a `DateTime`), then `HumanizeAiResponseValue` flattens a non-compliant structured response (the model sometimes returns a JSON array of finding/record groups despite instructions) into readable text rather than dumping raw JSON.
- **Storage and display:** on success, one `ScanResultAIInfo { ScanID, Insight, Recommendation }` row is saved to `dbo.ScanResultAI` and surfaces on the scan's AI Analysis tab (with short client-side polling while it's pending) and in the Excel export.
- **Failure isolation (NFR-014):** a timeout (bounded by `RequestTimeoutSeconds`), a missing/invalid API key, a network failure, or a malformed response are all caught *inside* the queued background work item and only logged -- none of them propagate back to the caller, so the already-durably-saved rule-based scan is unaffected either way.

(An earlier in-app documentation chatbot that shared this AI service was removed before v1.0.)

---

## 5. How file size determines the processing path

A file's size decides which of three tiers handles it, defined by two byte thresholds in `AuditTrailReview.WebApp/Pages/Scan/Scan.cshtml.cs:14-60`:

| Tier | Size | Where it runs | `ScanUploadJob` row? |
|---|---|---|---|
| 1 -- synchronous | ≤ 10 MB (`SyncThresholdBytes`) | Inline, in the HTTP request (WebApp only) | No |
| 2 -- Immediate | 10–50 MB (`ImmediateThresholdBytes`) | WebApp process, in-memory background queue | Yes (`Tier.Immediate`) |
| 3 -- Worker | 50 MB up to the outer ceiling | Integration Hub Worker Service, polling the DB | Yes (`Tier.Worker`) |

The outer ceiling is **786,432,000 bytes (750 MiB exactly)**, enforced via `[RequestSizeLimit]`/`[RequestFormLimits(MultipartBodyLengthLimit = OuterCeilingBytes)]` on both the WebApp's `ScanModel` and the API's upload action (previously 1,000,000,000 bytes/~954 MiB, raised at the time after testing against a real ~714 MB / 5-million-record file matching this project's own 5M-row scan benchmark; see §6 for the even earlier 500,000,000-byte figure).

**Tier 1 -- synchronous (≤ 10 MB, WebApp only):** parsed, mapped, rule-evaluated, and saved entirely inline inside `OnPostAsync`, blocking the HTTP request. Never touches `ScanUploadJob`.

**Tier 2 -- Immediate (10–50 MB, WebApp only):** `QueueLargeUploadAsync` buffers the whole file into memory, inserts a `ScanUploadJob { Tier = Immediate, Status = Queued, Source = WebApp, FileContent = bytes }` row, and immediately queues a work item on the *same process's* in-memory `IBackgroundTaskQueue` that claims the job (via a set-based `ExecuteUpdateAsync`, never a full tracked entity load) and runs `ScanUploadJobProcessor.RunAsync`. The uploader is redirected right away with a "queued" message. Because the bytes only ever live in that one process's memory, a restart mid-processing is unrecoverable: `ScanUploadJobProcessor.FailStuckImmediateJobsAsync` (called from the WebApp's own `Program.cs` at startup) marks any `Immediate` row still `Queued`/`Processing` as `Failed` ("Interrupted by an application restart -- please re-upload."); it is never retried.

**Tier 3 -- Worker (50 MB–750 MiB, any source):** a WebApp upload above 50 MB takes the same `QueueLargeUploadAsync` path but with `Tier = Worker` and is left `Queued` with no in-process queuing -- nothing happens until the Worker Service picks it up. **Every** Integration Hub API upload, regardless of size, also becomes a `Tier.Worker` row (`Source = Api`): the controller validates eagerly (400 for an unknown `SystemId` or unsupported extension, using the exact same message helpers `ProcessFileAsync` would otherwise throw later, so a bad request fails fast instead of becoming a doomed queued job), stores the file, and answers with **202 Accepted** and a `jobId`; callers (including the `AuditTrailReview.Console` CLI client) poll `GET api/ScanProcessing/scan/{jobId}` for status. **The API never parses or evaluates a file itself** -- it only ever stores a `Tier.Worker` row -- so a Worker Service instance must be running wherever the API is deployed, or every API-submitted job stays `Queued` forever.

`Tier.Worker` rows are drained by the Worker Service's `Worker.cs`, which calls `ScanUploadJobPoller.ProcessPendingAsync` on every `Ftp:PollingIntervalSeconds` tick, alongside (not instead of) its separate FTP file check in the same loop iteration. Each poll drains the *entire* queued backlog, oldest first, one job at a time -- processing is strictly sequential, so one very large file holds up every job queued behind it for as long as it takes. On startup, `ScanUploadJobProcessor.RecoverStuckWorkerJobsAsync` resets any `Tier.Worker` row left `Processing` from a crashed previous run back to `Queued` -- safe to do because, unlike `Tier.Immediate`, the bytes are durably stored in the database (`FileContent`), not held only in memory.

`ScanUploadJobProcessor.RunAsync` (shared by both Immediate and Worker tiers) streams `FileContent` and calls the same unmodified `ScanProcessingService.ProcessFileAsync` used by every other path. On success it clears `FileContent` and records the resulting `ScanId`. On failure, a `Tier.Worker` job with `RetryCount < MaxRetryAttempts` (3) is requeued (`Status` back to `Queued`, bytes untouched); a `Tier.Immediate` job, or a `Tier.Worker` job that has exhausted its retries, is marked `Failed` and its `FileContent` is cleared. The stored `ErrorMessage` always includes the true root-cause exception, not just EF's generic wrapper text (`BuildErrorMessage`).

**Why `FileContent` is never loaded through normal EF tracking:** a real ~700 MB blob loaded as a tracked entity property was observed taking 30+ minutes and pegging the CPU. Every status transition instead uses `ExecuteUpdateAsync` -- a set-based SQL UPDATE naming only the columns it touches -- on SQL Server and PostgreSQL, and the one place the bytes are genuinely needed (handing them to the parser) reads them via raw ADO.NET with `SequentialAccess`/streamed reads (`ReadFileContentStreamAsync` on SQL Server, a provider-neutral `OpenRelationalBlobStreamAsync` on PostgreSQL) rather than materializing the whole blob at once. EF Core's InMemory provider (used only by unit tests) falls back to a tracked load‑mutate‑`SaveChanges` path instead, since it doesn't support `ExecuteUpdate` and test fixtures are tiny anyway -- both implementations are kept manually in sync.

**The FTP ingestion path bypasses this tier system entirely.** `Worker.cs` hands each newly discovered file straight to `ScanProcessingService.ProcessFileAsync` with no size-based branching and no `ScanUploadJob` row at all -- deduplication there is tracked separately, in `dbo.IngestedFile`, keyed by filename per the Worker Service's one fixed `Ftp:SystemId`. The tiered queue was built for uploads that carry their own arbitrary `SystemId` (web and API), which doesn't fit the FTP model of one Worker instance per system.

---

## 6. Documentation/code drift corrected alongside this document

Found while writing this doc and fixed, code treated as the source of truth in every case:

1. **ADR-0000 and ADR-0003 said only the manual-upload path calls OpenAI** ("both bypass the AI-analysis step" / "which the other two ingestion paths do not do"). Corrected to the actual, current behavior: all ingestion paths queue AI analysis symmetrically when `EngineSettings.AiAnalysisEnabled` is true, since `ScanProcessingService.ProcessFileAsync` is shared by all of them. ADR-0005 and the PRD carried the same stale claim and were corrected too.
2. **`EngineSettings.AiAnalysisEnabled`'s XML doc comment and FR-035** said the same thing for the same reason and were corrected, along with the matching row in the Traceability Matrix.
3. **`Model/ScanUploadJob.cs`'s class comment quoted an outdated 500 MB outer size ceiling**; corrected to the actual, enforced 1,000,000,000-byte limit. ADR-0008 already flagged this comment as superseded; it now notes the fix.
4. **`dbo.EngineSettings` still had an unused `ReasonMinAlphabeticChars` column** left over from a retired rule (A5), never matched by anything in the current `EngineSettings` model class. A new script, `ATR 0.20 20261026 - EngineSettings Drop ReasonMinAlphabeticChars.sql`, drops the column and its default constraint; `EngineSettings.cs`'s class comment now points to it.
