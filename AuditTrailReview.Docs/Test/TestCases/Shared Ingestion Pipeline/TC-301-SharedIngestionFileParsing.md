# TC-301 – Shared Ingestion Pipeline: File Parsing

## Objective
Verify that `ScanProcessingService.ProcessFileAsync` (the shared ingestion pipeline used by both the Integration Hub API and, via the same code path, manual/Worker Service uploads) correctly parses CSV, JSON, and XML audit trail files, detects the CSV delimiter, maps file extensions to the correct `ATFileType`, and rejects unsupported extensions outright rather than silently mis-parsing them.

## Preconditions
- `ScanProcessingService` is constructed directly with an EF Core InMemory `ApplicationDbContext` and a `NullLogger`.
- All 19 rules are seeded into the database (disabled unless a test says otherwise).
- Minimal valid CSV/JSON/XML fixtures are used, each encoding one `Create` record with the same logical content.
- Historical note (see test comments): unknown/unsupported extensions used to silently fall back to CSV parsing (a gap tracked under NFR-020); `ProcessFileAsync` now rejects them outright instead.

## Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call `ProcessFileAsync` with the valid CSV fixture, filename `audit.csv` | Returns a scan id `> 0`; persisted `Scan.ATFileType == csv` and `ATFileName == "audit.csv"` |
| 2 | Call it with the valid JSON fixture, filename `audit.json` | Returns a scan id `> 0`; persisted `Scan.ATFileType == json` |
| 3 | Call it with the valid XML fixture, filename `audit.xml` | Returns a scan id `> 0`; persisted `Scan.ATFileType == xml` |
| 4 | Call it with all 19 rules enabled and the valid CSV fixture | Scan is persisted; any resulting `ScanResult` rows are linked to the scan (zero findings is a valid outcome if the fixture data passes every rule) |
| 5 | Call it with a CSV using `;` as the field delimiter instead of `,` | File parses successfully; scan id `> 0` |
| 6 | Call it with filenames `audit.CSV`, `audit.JSON`, `audit.XML` (mixed case) | Extension matching is case-insensitive; each maps to the correct `ATFileType` |
| 7 | Call it with filenames `audit.txt`, `audit.pdf`, and `audit` (no extension at all) | Each throws `InvalidOperationException`; no scan is persisted |

## Assertions
- `ATFileType` on the persisted `Scan` matches the file's extension, case-insensitively.
- An unsupported or missing extension throws `InvalidOperationException` and leaves `context.Scan` empty; it is never mis-parsed as CSV.
- A non-comma delimiter is auto-detected and does not break parsing.

## Related Code
- Test class: `AuditTrailReview.UnitTest.ScanProcessingServiceTests`
- Methods: `ProcessFileAsync_ValidCsvWithNoSystem_ReturnsScanId`, `ProcessFileAsync_ValidJsonWithNoSystem_ReturnsScanId`, `ProcessFileAsync_ValidXmlWithNoSystem_ReturnsScanId`, `ProcessFileAsync_ValidCsvPersistsScanResults`, `ProcessFileAsync_CsvWithSemicolonDelimiter_ParsesSuccessfully`, `ProcessFileAsync_FileNameExtension_SetsCorrectATFileType` (`[DataRow]`: `audit.CSV`/csv, `audit.JSON`/json, `audit.XML`/xml), `ProcessFileAsync_UnsupportedExtension_ThrowsInvalidOperationException` (`[DataRow]`: `audit.txt`, `audit.pdf`, `audit`)
- Production code: `AuditTrailReview.Engine.ScanProcessingService.ProcessFileAsync`
- Requirement: NFR-020

## Status
Active
