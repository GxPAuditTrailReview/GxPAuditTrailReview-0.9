# TC-402 – Large Upload Queue: `ScanUploadJobProcessor`

## Objective
Verify that the `dbo.ScanUploadJob` queue processor (`ScanUploadJobProcessor`) completes a valid upload, retries `Tier.Worker` failures a bounded number of times, never retries `Tier.Immediate` failures, claims queued worker jobs oldest first and only one at a time, and recovers jobs that were left in an unfinished state by a restart.

## Preconditions
- `ScanUploadJobProcessor` is constructed with an EF Core InMemory `ApplicationDbContext`, a real `ScanProcessingService`, and a `NullLogger`.
- The 19 rules are seeded and a valid CSV fixture is stored as `FileContent` (or kept in memory for the immediate tier).
- EF InMemory does not support `ExecuteUpdateAsync`, so these tests exercise the processor's tracked fallback. The set-based SQL path used on SQL Server and PostgreSQL is **not** covered here (see Notes).

## Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | `RunAsync` on a queued job holding a valid file | Status `Completed`, `ScanId` set, `FileContent` cleared |
| 2 | `RunAsync` on a `Tier.Immediate` job whose processing throws | Status `Failed` straight away (never requeued), bytes cleared, error message recorded |
| 3 | `RunAsync` on a `Tier.Worker` job that throws, below the retry limit | Status back to `Queued`, retry count incremented, claim timestamp cleared, file bytes intact |
| 4 | `RunAsync` on a `Tier.Worker` job that throws at the retry limit (`MaxRetryAttempts`, 3) | Status `Failed`, bytes cleared, finish time recorded |
| 5 | `ProcessNextPendingAsync` with no queued worker jobs, or only `Tier.Immediate` jobs | Returns `false`; nothing is claimed |
| 6 | `ProcessNextPendingAsync` with one queued worker job | The job is claimed and completed; returns `true` |
| 7 | `ProcessNextPendingAsync` with several queued worker jobs | The oldest job is claimed first |
| 8 | `RecoverStuckWorkerJobsAsync` with a `Tier.Worker` job left in `Processing` | Reset to `Queued`; `Tier.Immediate` jobs untouched |
| 9 | `FailStuckImmediateJobsAsync` with `Tier.Immediate` jobs in `Queued` or `Processing` | Marked `Failed`; `Completed` immediate jobs and all `Tier.Worker` jobs left alone |
| 10 | `BuildFileContentSelectSql` for the PostgreSQL and SQL Server providers | PostgreSQL quotes the mixed-case identifiers; SQL Server uses bracket quoting |
| 11 | `RunAsync` for a job that carries a `UserId` | The resulting `Scan.UserId` is that user, so "Uploaded by" survives the queue |

## Assertions
- A completed job always links to the resulting scan and releases its stored bytes.
- Retry behavior is bounded (three attempts for `Tier.Worker`) and absent for `Tier.Immediate`.
- Jobs are claimed one at a time in creation order, and only the worker tier is claimed by the Worker Service.
- Restart recovery changes only the tier it is meant to change.
- The generated SQL for reading the stored file is correct per provider.
- The uploader recorded on the job is carried onto the scan.

## Related Code
- Test classes: `AuditTrailReview.UnitTest.ScanUploadJobProcessorTests`, `AuditTrailReview.UnitTest.ScanUploadJobProcessorProviderSqlTests`
- Methods: `RunAsync_ValidFile_CompletesAndSetsScanId`, `RunAsync_JobWithUserId_SetsUserIdOnResultingScan`, `RunAsync_ImmediateTierFailure_FailsWithoutRequeuing`, `RunAsync_WorkerTierFailure_BelowMaxRetries_RequeuesWithBytesIntact`, `RunAsync_WorkerTierFailure_AtMaxRetries_FailsAndClearsBytes`, `ProcessNextPendingAsync_NoQueuedWorkerJobs_ReturnsFalse`, `ProcessNextPendingAsync_IgnoresImmediateTierJobs`, `ProcessNextPendingAsync_OneQueuedWorkerJob_ClaimsAndCompletesIt`, `ProcessNextPendingAsync_MultipleQueuedJobs_ClaimsOldestFirst`, `RecoverStuckWorkerJobsAsync_ProcessingWorkerJob_ResetToQueued`, `RecoverStuckWorkerJobsAsync_DoesNotTouchImmediateTierJobs`, `FailStuckImmediateJobsAsync_QueuedOrProcessingImmediateJob_MarkedFailed` (data-driven over two statuses), `FailStuckImmediateJobsAsync_DoesNotTouchWorkerTierJobs`, `FailStuckImmediateJobsAsync_CompletedImmediateJob_LeftAlone`, `BuildFileContentSelectSql_Postgres_QuotesMixedCaseIdentifiers`, `BuildFileContentSelectSql_SqlServer_UsesBracketQuoting`
- Production code: `AuditTrailReview.Engine.ScanUploadJobProcessor`, `AuditTrailReview.Model.ScanUploadJob`
- Requirement: FR-038, NFR-010, NFR-044

## Notes
- **Not covered by automated tests:** the `ExecuteUpdateAsync` (set-based) status transitions used on SQL Server and PostgreSQL, the live `SequentialAccess` stream read of `FileContent`, the `ScanUploadJobPoller` loop, and the notification poll (`Pages/Notifications`). They were verified by the large-file runs recorded in `Test/TestStrategy.md` (including the 5,000,000-record run) and remain manual.
- Keep the two implementations of each status helper (set-based and tracked) in sync; these tests only protect the tracked one.

## Status
Active
