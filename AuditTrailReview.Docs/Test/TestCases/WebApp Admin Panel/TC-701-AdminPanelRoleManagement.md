# TC-701 – WebApp Admin Panel: Role Management

## Objective
Verify the Admin Panel's Role CRUD page models (`Pages/AdminPanel/Role/{Index,Create,Edit,Details,Delete}.cshtml.cs`): listing/paging/searching roles, creating a role (including duplicate-name protection), editing a role's name, viewing a role's assigned users, and deleting a role, each with the correct audit-trail logging and redirect behavior.

## Preconditions
- Each page model is constructed with an EF Core InMemory `ApplicationDbContext` and a real `RoleManager<IdentityRole>` (via `PageModelTestHelpers.CreateRoleManager`), backed by the same context.
- `PageModelTestHelpers.InitializePageContext` wires up a signed-in `PageModel.User`/`ModelState`/`TempData` without a running host, so handlers can be invoked directly.

## Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | `IndexModel.OnGetAsync` with no roles, with multiple roles, and with more roles than one page size | Empty list when there are none; all roles returned ordered by name; pagination honors the page size and clamps a page number beyond the last page |
| 2 | `IndexModel.OnGetAsync` with a search term matching a role name, and matching nothing | Search filters across all roles, not just the current page; a non-matching term returns an empty result, not an error |
| 3 | `CreateModel.OnGet` | Returns the page |
| 4 | `CreateModel.OnPostAsync` with a new role name | The role is created, an audit "Create" entry is logged, and the handler redirects to the Index page |
| 5 | `CreateModel.OnPostAsync` with a new role name | Every logged audit entry carries the newly created role's Id in `TargetRoleId` |
| 6 | `CreateModel.OnPostAsync` with a role name that already exists | No duplicate role is created and no audit entry is logged |
| 7 | `EditModel.OnGetAsync` with a null/empty id, and with an unknown id | Returns `NotFound` in both cases |
| 8 | `EditModel.OnGetAsync` with an existing role id | Populates the role id and current name |
| 9 | `EditModel.OnPostAsync` with a null/empty id or an unknown id | Returns `NotFound` |
| 10 | `EditModel.OnPostAsync` with a changed name vs. an unchanged name | A changed name updates the role and logs an audit "Update" entry, carrying the edited role's Id in `TargetRoleId`; an unchanged name updates nothing and logs no audit entry |
| 11 | `EditModel.OnPostAsync` with a valid update | Redirects to the Index page |
| 12 | `DetailsModel.OnGetAsync` with a null/empty id, and with an unknown id | Returns `NotFound` |
| 13 | `DetailsModel.OnGetAsync` for a role with no users, and for a role with assigned users | `UsersInRole` is empty, or populated with the role's members, respectively |
| 14 | `DetailsModel.OnGetAsync` for a role's own "Audit trail" tab, with an entry for this role and one for a different role | Only the entry whose `TargetRoleId` matches the viewed role is included; the other role's entry is excluded |
| 15 | `DetailsModel.OnGetAsync` with `AuditTrailSearchTerm` set | Filters the role's audit entries to those matching the term |
| 16 | `DetailsModel.OnGetAsync` with more audit entries than one page size, requesting page 2 | Pagination honors `AuditTrailPageSize` and returns the correct `AuditTrailTotalPages`/remaining entries |
| 17 | `DeleteModel.OnGetAsync` with a null/empty id, and with an unknown id | Returns `NotFound` |
| 18 | `DeleteModel.OnGetAsync` with an existing role | Populates the role id and name for confirmation |
| 19 | `DeleteModel.OnPostAsync` with a null/empty id or an unknown id | Returns `NotFound` |
| 20 | `DeleteModel.OnPostAsync` with an existing role | Deletes the role, logs an audit "Delete" entry carrying the deleted role's Id in `TargetRoleId`, and redirects to the Index page |

## Assertions
- Every handler that takes an id guards against `null`/empty and unknown ids with `NotFound` before touching the database.
- Create/Edit/Delete log an audit-trail entry (via `AuditTrailService`, TC-801) only when a real change occurs, attributed to the signed-in user, and every such entry's `TargetRoleId` identifies the role that was created, edited, or deleted.
- Role name uniqueness is enforced on create.
- The Details page's "Audit trail" tab is scoped strictly to entries whose `TargetRoleId` matches the role being viewed, and supports search and paging.

## Related Code
- Test classes: `AuditTrailReview.UnitTest.WebApp.AdminPanel.Role.{IndexModelTests, CreateModelTests, EditModelTests, DetailsModelTests, DeleteModelTests}`
- Methods: all methods in `WebApp/AdminPanel/Role/IndexModelTests.cs`, `CreateModelTests.cs`, `EditModelTests.cs`, `DetailsModelTests.cs`, `DeleteModelTests.cs` -- including `OnPostAsync_NewRoleName_AuditEntryRecordsCreatedTargetRoleId`, `OnPostAsync_ExistingRole_AuditEntryRecordsDeletedTargetRoleId`, `OnPostAsync_NameChanged_AuditEntryRecordsEditedTargetRoleId`, and the `DetailsModelTests` Audit trail tab methods (`OnGetAsync_AuditTrailEntriesForThisRole_Included`, `OnGetAsync_AuditTrailEntriesFromOtherRole_NotIncluded`, `OnGetAsync_AuditTrailSearchTerm_FiltersToMatchingEntries`, `OnGetAsync_AuditTrailPaging_ReturnsRequestedPage`)
- Production code: `AuditTrailReview.WebApp.Pages.AdminPanel.Role.{IndexModel, CreateModel, EditModel, DetailsModel, DeleteModel}`
- Related: TC-801 (the `TargetRoleId` column written by `AuditTrailService`)

## Status
Active
