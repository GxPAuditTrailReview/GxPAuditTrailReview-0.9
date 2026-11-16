# TC-771 – WebApp: Audited System Management

## Objective
Verify the `System` entity's CRUD page models (`Pages/System/{Index,Create,Edit,Details,Delete}.cshtml.cs`), the audited systems (e.g. a specific MES or LIMS instance) that scans are assigned to, distinct from `SystemType` (TC-751), the category a system belongs to.

## Preconditions
- Each page model is constructed with an EF Core InMemory `ApplicationDbContext`; `PageModelTestHelpers.InitializePageContext` wires up a signed-in user.

## Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | `IndexModel.OnGetAsync` with an empty database, with multiple systems, and with more than one page size | Lists are empty but non-null when there are none; all systems returned ordered by system prefix id; pagination honors the page size, clamps a page number beyond the last page, and the second page returns the remainder |
| 2 | `IndexModel.OnGetAsync` with a search term matching system prefix id, system name, or system type name, and matching nothing | Each field is searchable; a non-matching term returns an empty result, not an error; the system-type dropdown list stays fully populated regardless of the search term |
| 3 | `CreateModel.OnGetAsync` with system types present, and with none | Populates the system-type list from the database, or an empty list when there are none |
| 4 | `CreateModel.OnPostAsync` with an invalid model state | Returns the page without saving, and repopulates the system-type list |
| 5 | `CreateModel.OnPostAsync` with a valid model | Saves the system, logs an audit "Create" entry (recording the system type's display name), and redirects to the Index page |
| 6 | `CreateModel.OnPostAsync` with a valid model | Every logged audit entry carries the newly created system's Id in `SystemID` |
| 7 | `CreateModel.OnPostAsync` with no user on the context | The audit entry's user id is `null` |
| 8 | `DetailsModel.OnGetAsync` with an existing id, a non-existent id, and a null id | Loads the system and its type for the first; `NotFound` for the other two |
| 9 | `DetailsModel.OnGetAsync` for a system with no type id, and for a system with multiple scans | Type info stays `null` when there's no type; scans are ordered by scan date descending and scans from other systems are excluded |
| 10 | `DetailsModel.OnGetAsync` for a system's own "Audit trail" tab, with an entry for this system and one for a different system | Only the entry whose `SystemID` matches the viewed system is included; the other system's entry is excluded |
| 11 | `DetailsModel.OnGetAsync` with an audit entry whose `SystemID` is `null` | Excluded from the tab |
| 12 | `DetailsModel.OnGetAsync` with `AuditTrailSearchTerm` set, and with more entries than one page size requesting page 2 | Filters to matching entries; pagination honors `AuditTrailPageSize` |
| 13 | `EditModel.OnGetAsync` with an existing id, a non-existent id, and a null id | Loads the system and type list for the first; `NotFound` for the other two |
| 14 | `EditModel.OnPostAsync` with an invalid model state | Returns the page without saving |
| 15 | `EditModel.OnPostAsync` with a changed name vs. no actual change | A changed name updates the database and logs an audit "Update" entry naming only the changed field, carrying the edited system's Id in `SystemID`; no change logs nothing |
| 16 | `EditModel.OnPostAsync` with a valid update | Redirects to the Index page |
| 17 | `DeleteModel.OnGetAsync` for a system with a type, without a type, with a non-existent id, and with a null id | Loads the system (and type when present) for the first two; `NotFound` for the other two |
| 18 | `DeleteModel.OnPostAsync` with an existing id | Deletes the system, logs an audit "Delete" entry carrying the deleted system's Id in `SystemID`, and redirects to the Index page |
| 19 | `DeleteModel.OnPostAsync` with a null id, and with a non-existent id | `NotFound`; and redirects without deleting or logging, respectively |

## Assertions
- Every handler that takes an id guards against `null` and unknown ids with `NotFound` before touching the database.
- Create/Edit log an audit-trail entry only for actual changes, and the Create entry records the assigned system type's display name; every Create/Edit/Delete entry's `SystemID` identifies the system involved.
- Index search covers system prefix id, system name, and system type name, without narrowing the system-type dropdown.
- The Details page's "Audit trail" tab is scoped strictly to entries whose `SystemID` matches the system being viewed (excluding entries with no `SystemID` at all), and supports search and paging.

## Related Code
- Test classes: `AuditTrailReview.UnitTest.WebApp.System.{IndexModelTests, CreateModelTests, EditModelTests, DetailsModelTests, DeleteModelTests}`
- Methods: all methods in `WebApp/System/IndexModelTests.cs`, `CreateModelTests.cs`, `EditModelTests.cs`, `DetailsModelTests.cs`, `DeleteModelTests.cs` -- including `OnPostAsync_ValidModel_AuditEntryRecordsCreatedSystemId`, `OnPostAsync_ExistingId_AuditEntryRecordsDeletedSystemId`, `OnPostAsync_ChangedName_AuditEntryRecordsEditedSystemId`, and the `DetailsModelTests` Audit trail tab methods (`OnGetAsync_AuditTrailEntriesForThisSystem_Included`, `OnGetAsync_AuditTrailEntriesFromOtherSystem_NotIncluded`, `OnGetAsync_AuditTrailEntriesWithNullSystemId_NotIncluded`, `OnGetAsync_AuditTrailSearchTerm_FiltersToMatchingEntries`, `OnGetAsync_AuditTrailPaging_ReturnsRequestedPage`)
- Production code: `AuditTrailReview.WebApp.Pages.System.{IndexModel, CreateModel, EditModel, DetailsModel, DeleteModel}`
- Related: TC-801 (the `SystemID` column written by `AuditTrailService`)

## Status
Active
