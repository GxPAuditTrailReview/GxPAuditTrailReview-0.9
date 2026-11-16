# TC-201 – Integration Hub API: Rejects Invalid or Missing-System Upload

## Objective
Verify that `POST /api/ScanProcessing/scan` (`ScanProcessingController.ScanProcessingService`) rejects a null file, an empty file, an unsupported file extension, and an unknown `systemId`, returning `400 Bad Request` with an informative message and storing nothing in every case.

## Preconditions
- `ScanProcessingController` is constructed directly (no live HTTP host) with an EF Core InMemory `ApplicationDbContext`. The unsupported-extension and unknown-system checks run at submit time using the same messages as `ScanProcessingService.ProcessFileAsync` (`GetUnsupportedFileTypeMessage`, `SystemNotFoundMessage`).
- The endpoint requires the `ScanWrite` API-key authorization policy in production; these unit tests call the controller action directly and are not exercising that policy layer.

## Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | POST with `file = null` | `400 Bad Request` with body `"File is required."`; no job stored |
| 2 | POST with a zero-length file | `400 Bad Request` with body `"File is required."`; no job stored |
| 3 | POST a valid CSV with `systemId = 999` (no such system seeded) | `400 Bad Request` whose message contains `"999"`; no job stored |
| 4 | POST a file named `audit.txt` (unsupported extension) with `systemId = 0` | `400 Bad Request` whose message contains `".txt"`; no job stored |
| 5 | POST a file named `audit.pdf` (unsupported extension) with a valid, seeded `systemId` | `400 Bad Request`; no job stored, confirming the extension check is independent of a valid system id |
| 6 | POST a CSV whose file name is 450 characters long (`ScanUploadJob.FileName` is a 450-character column) | `400 Bad Request`; no job stored |
| 7 | POST a file reported as one byte over `ScanProcessingService.MaxRecommendedUploadBytes` (the 750 MiB cap) | `400 Bad Request` whose message suggests splitting the file; no job stored |

## Assertions
- Result is a `BadRequestObjectResult` in every case.
- `context.ScanUploadJob.CountAsync()` is `0` after each rejected request.
- The response body names the specific problem (missing file, unknown system id, unsupported extension, file name too long, file too large) rather than a generic error.

## Related Code
- Test class: `AuditTrailReview.UnitTest.ScanProcessingControllerTests`
- Methods: `ScanProcessingService_NullFile_ReturnsBadRequest`, `ScanProcessingService_EmptyFile_ReturnsBadRequest`, `ScanProcessingService_UnknownSystemId_ReturnsBadRequestAndStoresNothing`, `ScanProcessingService_UnsupportedFileExtension_ReturnsBadRequestAndStoresNothing`, `ScanProcessingService_UnsupportedFileExtensionWithKnownSystem_ReturnsBadRequest`, `ScanProcessingService_FileNameTooLong_ReturnsBadRequestAndStoresNothing`, `ScanProcessingService_FileExceedsMaxRecommendedSize_ReturnsBadRequestAndStoresNothing`
- Production code: `AuditTrailReview.IntegrationHub.API.Controllers.ScanProcessingController.ScanProcessingService`, `AuditTrailReview.Engine.ScanProcessingService.GetUnsupportedFileTypeMessage`
- Requirement: NFR-020, NFR-021

## Status
Active
