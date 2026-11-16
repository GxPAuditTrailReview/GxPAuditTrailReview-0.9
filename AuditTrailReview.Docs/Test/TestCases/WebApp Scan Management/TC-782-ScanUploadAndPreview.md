# TC-782 – WebApp Scan Management: Scan Upload and Preview

## Objective
Verify `ScanModel` (`Pages/Scan/Scan.cshtml.cs`), the manual small-upload path that parses a file, runs the rule engine, persists the `Scan`/`ScanResult` rows, and queues AI analysis, and `PreviewModel` (`Pages/Scan/Preview.cshtml.cs`), the preview-only path that parses a file without saving anything, and separately exports previewed rows to Excel with a live "looks like a personal name" finding.

## Preconditions
- Both page models are constructed with an EF Core InMemory `ApplicationDbContext`; `PageModelTestHelpers.CreateFormFile` builds a real (non-mocked) `IFormFile` for upload handlers.
- `ScanModel`'s tests use a stub `IBackgroundTaskQueue` (`QueueBackgroundWorkItem`) to verify AI analysis is queued, matching `ScanProcessingServiceTests`' pattern (TC-301).

## Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | `ScanModel.OnGetAsync` with a null id, with an existing system id, and with an unknown id | No `System`-specific info without an id; populates `SystemATRInfo` for an existing system id; stays `null` for an unknown id; none of these return an error page |
| 2 | `ScanModel.OnPostAsync` with no file, and with an empty file | Returns the page without saving a scan |
| 3 | `ScanModel.OnPostAsync` with an unsupported extension | Adds a model error and returns the page without saving |
| 4 | `ScanModel.OnPostAsync` | Rule flags reflect the database (`Rules` table) and default to `false` when a rule row is missing; when an `EngineSettings` row exists it is applied to rule evaluation rather than the hard-coded defaults |
| 5 | `ScanModel.OnPostAsync` with a valid CSV, XML, and JSON file | Each parses rows/headers and maps them; the saved `Scan.ATFileType` matches the uploaded format and `ATFileName` is recorded |
| 6 | `ScanModel.OnPostAsync` with a query-string system id, with none, and with an unknown one | A known id is assigned to the new scan; no id saves `SystemId = 0`; an unknown id returns the page with a model error and saves nothing (and queues no AI work item) |
| 7 | `ScanModel.OnPostAsync` with a query-string id of `0` | Treated the same as no system id, not as an unknown id |
| 8 | `ScanModel.OnPostAsync` with a valid CSV | Saves exactly one `ScanResult` per mapped row, each linked to the new scan; queues exactly one AI-analysis background work item; redirects to the Details page with the new scan id |
| 9 | `PreviewModel.OnPostAsync` with no file, with an empty file, and with an unsupported extension | Returns the page without parsing (the first two); adds a model error for the third |
| 10 | `PreviewModel.OnPostAsync` with a valid CSV (comma- and semicolon-delimited, and with a blank row), a valid JSON array, a JSON object with an array property, and a valid XML file | Each parses rows and headers correctly; blank rows are skipped |
| 11 | `PreviewModel.OnPostAsync` with a valid file | `MappedRows` stays empty: preview parses but never maps/persists rows |
| 12 | `PreviewModel.OnPostExportExcel` with no `TempData`, and with empty `TempData` for the mapped-rows JSON | Returns the page without producing a file |
| 13 | `PreviewModel.OnPostExportExcel` with valid previewed rows | Returns an Excel file with the expected headers and data |
| 14 | `PreviewModel.OnPostExportExcel` with a row whose `UserID` looks like a personal name, and with one that doesn't | The personal-name finding is flagged for the first and not the second (via `LoginNameHeuristics`, TC-112) |

## Assertions
- `ScanModel.OnPostAsync` never saves a `Scan` or queues AI analysis when the upload is missing, empty, unsupported, or targets an unknown system id.
- Rule evaluation on upload reflects whatever `Rules`/`EngineSettings` rows exist at the time, not compiled-in defaults.
- `PreviewModel` never persists anything to the database regardless of how it's called; its Excel export reflects only what was in `TempData`.

## Related Code
- Test classes: `AuditTrailReview.UnitTest.WebApp.Scan.ScanModelTests`, `AuditTrailReview.UnitTest.WebApp.Scan.PreviewModelTests`
- Methods: all methods in `WebApp/Scan/ScanModelTests.cs`, `WebApp/Scan/PreviewModelTests.cs`
- Production code: `AuditTrailReview.WebApp.Pages.Scan.ScanModel`, `AuditTrailReview.WebApp.Pages.Scan.PreviewModel`
- Related: both call into `AuditTrailFileParser` (TC-304) and `AuditTrailMapper` (TC-305); `ScanModel` additionally calls `ScanResultService.ApplyRulesAndGenerateResults` (TC-101–TC-111)

## Status
Active
