# TC-202 – Integration Hub API: Accepts Valid Upload

## Objective
Verify that `POST /api/ScanProcessing/scan` (`ScanProcessingController.ScanProcessingService`) accepts a valid CSV file, stores exactly one `Tier.Worker` `ScanUploadJob` (bytes preserved, `Source = Api`) for the Worker Service and returns `202 Accepted` without processing it, and, when a known `systemId` is supplied, links the job to that system.

## Note on request-size limit coverage
`ScanProcessingController`'s `[RequestSizeLimit]`/`[RequestFormLimits]` (786,432,000 bytes / 750 MiB) are enforced by the ASP.NET Core hosting pipeline before a request ever reaches the controller action. Because these tests construct and call the controller directly, in-process, with no live HTTP host (see Preconditions below), they cannot and do not exercise that limit -- a file that would be rejected at the HTTP layer for being too large is still accepted here. Confirming the limit itself requires a live-hosted integration test or manual verification (see `Installation Manual.md` §8 / `Technical Manual.md`'s IntegrationHub.API config table); out of scope for this OQ-documentation pass, same rationale as TC-401's automation note.

## Preconditions
- `ScanProcessingController` is constructed with an EF Core InMemory `ApplicationDbContext` (it no longer depends on `ScanProcessingService`; processing is covered by the Worker Service tests).
- A minimal valid CSV fixture is used: header row `Id,User,Timestamp,Action,Before,After,Reason` plus one `Create` record.

## Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | POST the valid CSV with `systemId = 0` (no system) | `202 Accepted`; exactly one `ScanUploadJob` (`Tier.Worker`, `Queued`, `Source = Api`, file bytes stored); no `Scan` created by the API |
| 2 | Seed a `SystemATRInfo` row, then POST the same valid CSV with that system's id | `202 Accepted`; the stored job's `SystemId` equals the seeded system's id |
| 3 | POST a multi-MB file | `202 Accepted`, queued exactly like a small file (no size-based routing) |
| 4 | POST `audit.json`, then `audit.xml` | `202 Accepted`; the stored job's `ATFileType` is JSON, then XML, matching the extension |
| 5 | `GetScanStatus` for a job id that does not exist | `404 Not Found` |
| 6 | `GetScanStatus` for a `Completed` job linked to scan 7 | `200 OK` with the status and the `ScanId`, which is what the Console client polls for after the `202` |

## Assertions
- Result is an `AcceptedResult` in every case.
- `context.ScanUploadJob` holds exactly one job with the uploaded bytes and `context.Scan` stays empty.
- The stored job's `SystemId` matches the seeded system id when one is supplied.
- The stored `ATFileType` follows the file extension.
- `GET api/ScanProcessing/scan/{jobId}` returns 404 for an unknown job and the job status plus scan id for a completed one.

## Related Code
- Test class: `AuditTrailReview.UnitTest.ScanProcessingControllerTests`
- Methods: `ScanProcessingService_ValidCsvNoSystem_ReturnsAcceptedAndStoresWorkerJob`, `ScanProcessingService_ValidCsvWithKnownSystem_ReturnsAcceptedAndLinksSystem`, `ScanProcessingService_LargeFile_IsStillQueuedForTheWorker`, `ScanProcessingService_JsonOrXml_StoresMatchingFileType` (data-driven: JSON and XML), `GetScanStatus_UnknownJob_ReturnsNotFound`, `GetScanStatus_CompletedJob_ReturnsStatusAndScanId`
- Production code: `AuditTrailReview.IntegrationHub.API.Controllers.ScanProcessingController` (`ScanProcessingService`, `GetScanStatus`), `AuditTrailReview.Engine.ScanUploadJobProcessor`
- Requirement: FR-018 (ingestion path), NFR-021

## Status
Active
