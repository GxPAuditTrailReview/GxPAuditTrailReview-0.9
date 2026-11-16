# TC-401 – Worker Service: FTP Polling and Ingestion (Manual Procedure)

## Objective
Verify that the Worker Service (hosted inside `AuditTrailReview.IntegrationHub.WorkerService`) periodically polls the configured FTP location for new audit trail files and, when one is found, ingests it through the same shared pipeline covered by TC-301–TC-303.

## Status of automation
**Manual, not yet automated.** Unlike TC-101 through TC-303, this script has **no Related Code section** and is not paired with an automated test. `FtpFileCheckerService.CheckForNewFilesAsync` constructs its FTP client directly (`new AsyncFtpClient(_settings.Host, _settings.Username, _settings.Password)`, via FluentFTP) with no injected or mockable abstraction, so there is currently nothing a unit test can substitute: the FTP-polling layer has zero automated coverage today. Automating this would require introducing an `IFtpClient`-style seam in `FtpFileCheckerService` first; that is a production-code change, not a test-writing one, and is deliberately out of scope for this OQ-documentation pass (item 17). This is recorded here explicitly rather than silently skipped or silently forced into the automated pattern.

## Preconditions
- The Worker Service (`AuditTrailReview.IntegrationHub.WorkerService`) is running and configured with a reachable FTP host, credentials, and polling interval.
- A test or staging FTP location is available and reachable from the machine running the Worker Service.
- `AuditTrailReview.Console` is a separate, explicitly-not-hosted component and is out of scope for this script.

## Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Start the Worker Service with valid FTP configuration | Application logs show the periodic "Checking FTP for new audit trail files" message at the configured interval (see `Technical Manual.md` §6 for the current practical monitoring approach) |
| 2 | Place a valid audit trail file (CSV/JSON/XML) in the configured FTP location | On the next poll, the Worker Service detects and downloads the file |
| 3 | Observe the application database after the poll | A new `Scan` row is persisted with `ATFileType`/`ATFileName` matching the uploaded file, via the same `ScanProcessingService.ProcessFileAsync` path exercised by TC-301–TC-303 |
| 3a | Observe the FTP location after the same poll | The file is no longer in `RemoteFolder` -- it has been moved into the configured `ProcessedFolder` subfolder (default `Processed`), which was created automatically if it didn't already exist |
| 4 | Place a file with an unsupported extension in the FTP location | The Worker Service logs the rejection; no `Scan` row is persisted (same validation as TC-301, step 7), but an `IngestedFile` row *is* created for the file (claimed `Status = Processing` before the extension check runs, then flipped to `Failed` with the extension error recorded in `ErrorMessage`), so the failed attempt is not silently invisible |
| 5 | Stop the FTP server or make the location unreachable, then observe the next poll | The Worker Service logs the failure and continues polling on its next scheduled interval rather than crashing |
| 6 | Place a large audit trail file on the FTP location; once its `IngestedFile` row is confirmed `Status = Processing` but before it completes, forcibly stop the Worker Service process, then restart it | On startup, before polling resumes, the Worker Service resets the file's `IngestedFile` row from `Processing` back to `Pending` (`FtpFileCheckerService.RecoverStuckClaimsAsync`); on the next poll the file is downloaded and fully reprocessed from scratch, resulting in exactly one new `Scan` row (not zero, not a duplicate) with a complete set of `ScanResult` rows |
| 7 | Cause the same file to fail processing repeatedly across multiple polls (e.g. reuse the unsupported-extension file from Step 4) | After 3 failed attempts, the file's `IngestedFile` row remains `Failed` with `RetryCount` at the configured maximum (`MaxRetryAttempts`); subsequent polls no longer attempt to reprocess it |
| 8 | Observe the FTP location after Step 7's final attempt | The file has been moved out of `RemoteFolder` into the configured `FailedFolder` subfolder (default `Failed`), created automatically if it didn't already exist |
| 9 | Configure the FTP account with read/download permission only (no write/rename/create-directory), then repeat Step 2 | The file is still detected, downloaded, and processed into a `Scan` row exactly as in Steps 2–3; the Worker Service logs a warning that it could not move the file to `ProcessedFolder`, and the file remains in `RemoteFolder` -- the missing permission does not affect ingestion or the `IngestedFile` row's `Completed` status |
| 10 | Continuing from Step 9 (file still stuck in `RemoteFolder` after a failed move), restore the FTP account's write permission, then wait past `Ftp:StaleUnmovedFileThresholdMinutes` and let a poll occur after `Ftp:StaleFileCheckIntervalMinutes` has elapsed since the last sweep (or temporarily lower both settings for the test) | The next due sweep logs "Found 1 file(s) still in ... that should have been moved out ..." and successfully moves the file into `ProcessedFolder` -- without reprocessing it or creating a second `Scan` row |
| 11 | Manually place a file with the same name directly into `ProcessedFolder` (e.g. from a prior run), then let a newly processed file of that same name be moved into `ProcessedFolder` on top of it | The move still succeeds -- the pre-existing file at the destination is detected and deleted first, then the newly processed file is moved into place, rather than the FTP rename failing with a `550` error |

## Assertions
- A valid file placed on the FTP server results in exactly one new `Scan` row after the next poll interval.
- An unsupported file extension is rejected without persisting a `Scan` row, consistent with the manual-upload and API paths, but is still recorded as a `Failed` claim in `dbo.IngestedFile`.
- A transient FTP failure is logged and does not stop the Worker Service's polling loop.
- A file interrupted mid-processing by a Worker Service crash/restart is fully reprocessed exactly once after restart: not skipped, and not duplicated.
- A file that fails repeatedly is retried up to the configured maximum (3 attempts) and then left alone, not retried indefinitely.
- A successfully processed file is moved into `ProcessedFolder`; a permanently-failed file is moved into `FailedFolder`; either subfolder is created automatically when first needed.
- An FTP account lacking write/rename permission can still ingest files successfully -- only the housekeeping move is affected, logged as a warning, never as a processing failure.
- A file whose original move to `ProcessedFolder`/`FailedFolder` failed is automatically retried by the periodic stale-file sweep once it's been overdue longer than `StaleUnmovedFileThresholdMinutes`, without being reprocessed or duplicated.
- A same-named file already present at the move destination does not block the move -- it's deleted first, then the move is retried, rather than failing with an FTP `550` rename error.

## Related Code
None at present; see "Status of automation" above.

## Status
Manual, not yet automated
