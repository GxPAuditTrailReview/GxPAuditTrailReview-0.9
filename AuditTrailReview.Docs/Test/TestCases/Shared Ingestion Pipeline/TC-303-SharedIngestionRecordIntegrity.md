# TC-303 – Shared Ingestion Pipeline: Record Integrity

## Objective
Verify that `ScanProcessingService.ProcessFileAsync` records the scan date as UTC "now" at the time of processing, honors cooperative cancellation, and generates a distinct scan id for each independent call.

## Preconditions
- `ScanProcessingService` is constructed directly with an EF Core InMemory `ApplicationDbContext` and a `NullLogger`.
- All 19 rules are seeded into the database.
- The valid CSV fixture is used for every case.

## Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Pre-cancel a `CancellationTokenSource` and pass its token into `ProcessFileAsync` | Throws `OperationCanceledException`; processing does not proceed |
| 2 | Record `DateTime.UtcNow` immediately before and after calling `ProcessFileAsync` with the valid fixture | The persisted `Scan.ScanDate` falls between the two UTC timestamps (measured in UTC specifically; see test comment on why a local-time comparison previously broke outside UTC-offset-zero environments) |
| 3 | Call `ProcessFileAsync` twice in succession with two different filenames | The two calls return different scan ids |
| 4 | Call `ProcessFileAsync` with `userId = "user-123"`, then without a user id | The saved `Scan.UserId` is `user-123`; without a user id it stays null (FTP and API ingestion) |
| 5 | Process a valid CSV | A `ScanFile` row for the new scan holds the original file bytes unchanged (the Scan Details download) |
| 6 | Process a file named `audit.txt` (unsupported extension) | Throws `InvalidOperationException`; no `ScanFile` row is saved |
| 7 | `GetFileTooLargeMessage` at exactly `MaxRecommendedUploadBytes`, one byte over it, and well under it | `null`, a message naming the file and suggesting to split it, and `null` |

## Assertions
- A cancelled token short-circuits processing via `OperationCanceledException` rather than completing.
- `ScanDate` is recorded in UTC and falls within the call's actual wall-clock window.
- Repeated calls never reuse a scan id.
- The uploader is recorded on the scan only when a user id is passed.
- The original bytes are stored with the scan, and nothing is stored for a rejected file.
- The size limit is inclusive at the threshold and the message is actionable.

## Related Code
- Test class: `AuditTrailReview.UnitTest.ScanProcessingServiceTests`
- Methods: `ProcessFileAsync_CancelledToken_ThrowsOperationCanceledException`, `ProcessFileAsync_ValidFile_ScanDateIsSetToApproximatelyNow`, `ProcessFileAsync_CalledTwice_ReturnsDifferentScanIds`, `ProcessFileAsync_WithUserId_SetsUserIdOnScan`, `ProcessFileAsync_NoUserId_LeavesUserIdNull`, `ProcessFileAsync_ValidCsv_SavesOriginalFileBytesToScanFile`, `ProcessFileAsync_UnsupportedExtension_DoesNotPersistScanFile`, `GetFileTooLargeMessage_AtThreshold_ReturnsNull`, `GetFileTooLargeMessage_OneByteOverThreshold_ReturnsActionableMessage`, `GetFileTooLargeMessage_WellUnderThreshold_ReturnsNull`
- Production code: `AuditTrailReview.Engine.ScanProcessingService.ProcessFileAsync`
- Requirement: NFR-042 (UTC timestamp consistency)

## Status
Active
