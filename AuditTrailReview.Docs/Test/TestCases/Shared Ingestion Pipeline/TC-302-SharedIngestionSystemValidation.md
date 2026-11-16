# TC-302 – Shared Ingestion Pipeline: System Validation

## Objective
Verify that `ScanProcessingService.ProcessFileAsync` correctly links an upload to an existing system, rejects an upload against a non-existent `systemId`, and treats `systemId = 0` as "no system" rather than as an unknown-id error, the same validation now shared consistently by the manual-upload page (`Scan.cshtml.cs`), the Integration Hub API, and the Worker Service, all of which call this one method (see item 11 in `Path to v1.0.md`).

## Preconditions
- `ScanProcessingService` is constructed directly with an EF Core InMemory `ApplicationDbContext` and a `NullLogger`.
- All 19 rules are seeded into the database.
- The valid CSV fixture is used for every case; only the `systemId` argument varies.

## Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Seed a `SystemATRInfo` row (`Id = 5`), then call `ProcessFileAsync` with `systemId: 5` | The persisted scan's `SystemID` equals `5` |
| 2 | Call `ProcessFileAsync` with `systemId: 999` and no matching system seeded | Throws `InvalidOperationException`; no scan persisted |
| 3 | Call `ProcessFileAsync` with `systemId: 0` (the default) and no system seeded | Returns a scan id `> 0`: the system lookup is skipped entirely for id `0`, by design (Worker Service and API uploads with no system id must still succeed) |

## Assertions
- A real, non-zero `systemId` that does not resolve to a `SystemATRInfo` row throws `InvalidOperationException` and persists nothing.
- `systemId == 0` is never treated as an unknown id; it is the shared "no system assigned" sentinel used consistently across all three ingestion paths.
- A resolving `systemId` is written onto the persisted scan's `SystemID`.

## Related Code
- Test class: `AuditTrailReview.UnitTest.ScanProcessingServiceTests`
- Methods: `ProcessFileAsync_ValidCsvWithExistingSystem_AssignsScanToSystem`, `ProcessFileAsync_NonExistentSystemId_ThrowsInvalidOperationException`, `ProcessFileAsync_SystemIdZero_DoesNotValidateSystem_ReturnsScanId`
- Production code: `AuditTrailReview.Engine.ScanProcessingService.ProcessFileAsync`
- Requirement: NFR-021

## Status
Active
