# TC-784 – WebApp Scan Management: Scan Editing and Deletion

## Objective
Verify `EditModel` (`Pages/Scan/Edit.cshtml.cs`), editing a scan's own metadata fields, and `DeleteModel` (`Pages/Scan/Delete.cshtml.cs`), deleting a scan together with its related `ScanResult` rows, without affecting any other scan's results.

## Preconditions
- Both page models are constructed with an EF Core InMemory `ApplicationDbContext`; `PageModelTestHelpers.InitializePageContext` wires up a signed-in user. `DeleteModelTests` disposes its context per test (`Dispose`).

## Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | `EditModel.OnGetAsync` with a null id, and an unknown id | Returns `NotFound` |
| 2 | `EditModel.OnGetAsync` with an existing scan | Populates the scan |
| 3 | `EditModel.OnPostAsync` with an invalid model state | Returns the page without saving |
| 4 | `EditModel.OnPostAsync` with a valid model | Updates the scan in the database and redirects to the Index page |
| 5 | `EditModel.OnPostAsync` with a non-existent scan | Returns `NotFound` |
| 6 | `DeleteModel.OnGetAsync` with a null id, and an unknown id | Returns `NotFound` |
| 7 | `DeleteModel.OnGetAsync` with an existing scan | Populates the scan with its `System` included |
| 8 | `DeleteModel.OnPostAsync` with a null id | Returns `NotFound` |
| 9 | `DeleteModel.OnPostAsync` with an unknown id | Redirects without deleting anything |
| 10 | `DeleteModel.OnPostAsync` with an existing scan | Deletes the scan and its related `ScanResult` rows |
| 11 | `DeleteModel.OnPostAsync` with an existing scan that has sibling scans | Other scans' `ScanResult` rows are left untouched |
| 12 | `DeleteModel.OnPostAsync` with an existing scan | Redirects to the Index page |

## Assertions
- Both handlers guard against `null`/unknown ids with `NotFound` (or a no-op redirect for delete-by-unknown-id) before touching the database.
- Deleting a scan removes exactly its own `ScanResult` rows and no others.

## Related Code
- Test classes: `AuditTrailReview.UnitTest.WebApp.Scan.EditModelTests`, `AuditTrailReview.UnitTest.WebApp.Scan.DeleteModelTests`
- Methods: all methods in `WebApp/Scan/EditModelTests.cs`, `WebApp/Scan/DeleteModelTests.cs`
- Production code: `AuditTrailReview.WebApp.Pages.Scan.EditModel`, `AuditTrailReview.WebApp.Pages.Scan.DeleteModel`

## Status
Active
