# TC-751 – WebApp Config Panel: System Type Management

## Objective
Verify the Config Panel's System Type CRUD page models (`Pages/ConfigPanel/SystemType/{Index,Create,Edit,Details,Delete}.cshtml.cs`): listing/paging/searching system types, creating one, editing one, viewing one, and deleting one, including the in-use guard that blocks deleting a system type still referenced by a `System` row.

## Preconditions
- Each page model is constructed with an EF Core InMemory `ApplicationDbContext`; `PageModelTestHelpers.InitializePageContext` wires up a signed-in user.

## Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | `IndexModel.OnGetAsync` with no system types, with multiple, and with more than one page size | Empty list when there are none; all returned ordered by system type; pagination honors the page size and clamps a page number beyond the last page |
| 2 | `IndexModel.OnGetAsync` with a search term matching a system type, and matching nothing | Filters across all system types, not just the current page; a non-matching term returns an empty result |
| 3 | `CreateModel.OnGet` | Returns the page |
| 4 | `CreateModel.OnPostAsync` with an invalid model state | Returns the page without saving |
| 5 | `CreateModel.OnPostAsync` with a valid model | Saves the system type, logs an audit "Create" entry, and redirects to the Index page |
| 6 | `CreateModel.OnPostAsync` with a valid model | Every logged audit entry carries the newly created system type's Id in `SystemTypeID` |
| 7 | `CreateModel.OnPostAsync` with no user on the context | The audit entry's user id is `null` |
| 8 | `DetailsModel.OnGetAsync` with an existing id, a non-existent id, and a null id | Loads the system type for the first; `NotFound` for the other two |
| 9 | `DetailsModel.OnGetAsync` for a system type's own "Audit trail" tab, with an entry for this system type and one for a different one | Only the entry whose `SystemTypeID` matches the viewed system type is included; the other's is excluded |
| 10 | `DetailsModel.OnGetAsync` with a rule-enablement audit entry present (logged by TC-752 under the same `AuditObjectType.ConfigPanel` but with `SystemTypeID` left `null`) | Excluded -- the tab is scoped to `SystemTypeID`, not just the shared `ObjectType` |
| 11 | `DetailsModel.OnGetAsync` with `AuditTrailSearchTerm` set, and with more entries than one page size requesting page 2 | Filters to matching entries; pagination honors `AuditTrailPageSize` |
| 12 | `EditModel.OnGetAsync` with an existing id, a non-existent id, and a null id | Loads the system type for the first; `NotFound` for the other two |
| 13 | `EditModel.OnPostAsync` with an invalid model state | Returns the page without saving |
| 14 | `EditModel.OnPostAsync` with a changed value vs. no actual change | A change updates the database and logs an audit "Update" entry carrying the edited system type's Id in `SystemTypeID`; no change logs nothing |
| 15 | `EditModel.OnPostAsync` with a valid update | Redirects to the Index page |
| 16 | `DeleteModel.OnGetAsync` for a system type not referenced by any `System` row, and for one that is referenced | `IsInUse` is `false` or `true` respectively |
| 17 | `DeleteModel.OnGetAsync` with a non-existent id, and a null id | `NotFound` |
| 18 | `DeleteModel.OnPostAsync` for a system type not in use | Deletes it from the database, logs an audit "Delete" entry carrying the deleted system type's Id in `SystemTypeID`, and redirects to the Index page |
| 19 | `DeleteModel.OnPostAsync` for a system type that is in use, for a null id, and for a non-existent id | In-use: redirects without deleting or logging; the other two also redirect/`NotFound` without deleting or logging |

## Assertions
- Every handler that takes an id guards against `null` and unknown ids with `NotFound` before touching the database.
- Create/Edit log an audit-trail entry only for actual changes, and every Create/Edit/Delete entry's `SystemTypeID` identifies the system type involved.
- Delete is blocked for any system type still referenced by a `System` row, with no database change and no audit entry in that case.
- The Details page's "Audit trail" tab is scoped strictly to entries whose `SystemTypeID` matches the system type being viewed -- a rule-enablement change sharing the same `ObjectType` but with a `null` `SystemTypeID` must not appear -- and supports search and paging.

## Related Code
- Test classes: `AuditTrailReview.UnitTest.WebApp.ConfigPanel.SystemType.{IndexModelTests, CreateModelTests, EditModelTests, DetailsModelTests, DeleteModelTests}`
- Methods: all methods in `WebApp/ConfigPanel/SystemType/IndexModelTests.cs`, `CreateModelTests.cs`, `EditModelTests.cs`, `DetailsModelTests.cs`, `DeleteModelTests.cs` -- including `OnPostAsync_ValidModel_AuditEntryRecordsCreatedSystemTypeId`, `OnPostAsync_NotInUse_AuditEntryRecordsDeletedSystemTypeId`, `OnPostAsync_ChangedSystemType_AuditEntryRecordsEditedSystemTypeId`, and the `DetailsModelTests` Audit trail tab methods (`OnGetAsync_AuditTrailEntriesForThisSystemType_Included`, `OnGetAsync_AuditTrailEntriesFromOtherSystemType_NotIncluded`, `OnGetAsync_AuditTrailEntriesFromRuleEnablement_NotIncluded`, `OnGetAsync_AuditTrailSearchTerm_FiltersToMatchingEntries`, `OnGetAsync_AuditTrailPaging_ReturnsRequestedPage`)
- Production code: `AuditTrailReview.WebApp.Pages.ConfigPanel.SystemType.{IndexModel, CreateModel, EditModel, DetailsModel, DeleteModel}`
- Requirement: CT-09 (System Type Management, per the IQOQ protocol)
- Related: TC-801 (the `SystemTypeID` column written by `AuditTrailService`)

## Status
Active
