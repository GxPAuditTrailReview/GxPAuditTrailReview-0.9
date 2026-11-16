# TC-781 – WebApp Scan Management: Scan List, Filtering, and System Assignment

## Objective
Verify `IndexModel` (`Pages/Scan/Index.cshtml.cs`), the main scan list page: paging, ordering, free-text search across several fields including year and exact-date matching, and the inline "assign a system to this scan" action.

## Preconditions
- `IndexModel` is constructed with an EF Core InMemory `ApplicationDbContext`; `PageModelTestHelpers.InitializePageContext` wires up a signed-in user.

## Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | `OnGetAsync` with no scans, with scans present, and with more scans than one page size | Empty list and one page when there are none; scans ordered by scan date descending; pagination honors the page size, clamps a page number beyond the last page, clamps a page number less than 1 up to 1, and the second page returns the remainder |
| 2 | `OnGetAsync` | Populates the systems list and attaches each scan's `System` navigation property; also populates the page numbers to display, including the first page |
| 3 | `OnGetAsync` with a search term matching system name, system prefix, file name, or file type, and matching nothing | Each field is searchable across all scans, not just the current page; a non-matching term returns an empty list, not every scan |
| 4 | `OnGetAsync` with a search term that is a 4-digit year, and with a search term that is an exact date | Filters to scans in that year, or to that exact day only, respectively |
| 5 | `OnGetAsync` with a whitespace-only search term | Treated as no search: all scans returned unfiltered |
| 6 | `OnGetAsync` with a search term and an explicit page number | Setting a search term resets paging logic but still honors an explicitly requested page number |
| 7 | `OnGetAsync` with `SystemId = 0` while also searching | A scan with no system assigned (`SystemId == 0`) is never shown as having a system, even when searching |
| 8 | `OnPostAssignSystemAsync` with an unknown scan id | Returns `NotFound` |
| 9 | `OnPostAssignSystemAsync` with an existing scan | Updates the scan's `SystemId` |
| 9a | `OnPostAssignSystemAsync` with an existing scan, as a signed-in user; and again for a scan whose `UserId` is already another user's | The scan's `UserId` is set to the assigning user's id in the same save as the system (making it a "user scan"); an earlier `UserId` (e.g. the original uploader) is overwritten |
| 10 | `OnPostAssignSystemAsync` | Preserves the current page number and search term on the redirect back to Index |
| 11 | Call `GetEnumDisplayName` for an enum value with a `[Display]` attribute, and for one without | Returns the `[Display]` name, or falls back to `ToString()` respectively |

## Assertions
- Search matches system name, system prefix, file name, file type, a 4-digit year, or an exact date, applied across the full scan set before paging.
- Paging always clamps to a valid page number (never below 1, never past the last page).
- Assigning a system to a scan preserves the caller's current page and search term on redirect.
- Assigning a system to a scan also stores the assigning user's id (`NameIdentifier` claim) in `ScanInfo.UserId`, replacing any previous value.

## Related Code
- Test class: `AuditTrailReview.UnitTest.WebApp.Scan.IndexModelTests`
- Methods: all methods in `WebApp/Scan/IndexModelTests.cs`
- Production code: `AuditTrailReview.WebApp.Pages.Scan.IndexModel`

## Status
Active
