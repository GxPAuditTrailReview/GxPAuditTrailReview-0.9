# TC-810 – Log Pages Restricted to Admin and Super User

## Objective
Verify that the two system-wide log pages, `Log/UserLogin` (sign-in history with user names, IP addresses and failed attempts) and `Log/AuditTrail` (the application's change history across all records), require the Admin or Super User role, so a plain "User" can neither open them nor see the **Logs** menu. The per-record "Audit trail" tabs on System, Scan, System type, User and Role pages are separate and keep their own access rules.

## Preconditions
- The check is done by reflection over the compiled page models (`UserLoginModel`, `AuditTrail`), the same approach as `PageAuthorizationConventionTests`. No host or database is needed.

## Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Read the `[Authorize]` attribute(s) on `UserLoginModel` | Roles are exactly "Admin" and "Super User" |
| 2 | Read the `[Authorize]` attribute(s) on `AuditTrail` | Roles are exactly "Admin" and "Super User" |
| 3 | Check both for the role "User" | Not present |
| 4 | `UserLoginModel.OnGetExportAsync` with a search term that matches nothing and a page number set, over two log entries | An Excel file (`User Login Log` sheet) with every entry, newest first, with Success shown as Yes/No: the export ignores search and paging |

## Assertions
- Both log pages carry a role-restricted `[Authorize]`, not a bare sign-in requirement.
- The Excel export contains the whole login log regardless of the page's search term or paging.
- A plain User is redirected by the cookie handler to the Access denied page (HTTP 403 behaviour) when opening either URL.

## Related Code
- Test classes: `AuditTrailReview.UnitTest.Security.LogPagesAuthorizationTests`, `AuditTrailReview.UnitTest.WebApp.Log.UserLoginModelTests`
- Methods: `UserLoginModel_Authorize_RequiresAdminOrSuperUser`, `AuditTrail_Authorize_RequiresAdminOrSuperUser`, `LogPages_Authorize_DoNotAllowPlainUserRole`, and from `AuditTrailReview.UnitTest.WebApp.Log.UserLoginModelTests`: `OnGetExportAsync_ReturnsExcelWithAllEntriesRegardlessOfSearchOrPaging`
- Production code: `AuditTrailReview.WebApp.Pages.Log.UserLoginModel`, `AuditTrailReview.WebApp.Pages.Log.AuditTrail`, `Pages/Shared/_Layout.cshtml` (the Logs menu is shown only to Admin and Super User)
- Related: TC-603 / FT-15 (role-based navigation), TC-809 (the Access denied page a plain User lands on)

## Notes
- The menu visibility and the redirect were checked by hand on 2026-11-05 against the WebApp on the SQLite configuration, with one account per role created through Admin Panel → Users: Admin and Super User got HTTP 200 on both pages and saw the Logs menu; the plain User was redirected to `/Identity/Account/AccessDenied` for both URLs, saw no Logs menu, and the Access denied page rendered. A plain User could still open Systems (HTTP 200).
- Not covered by an automated test: the rendered menu and the live redirect. The Selenium and Playwright suites sign in as the seeded Admin, so they only prove the menu is present for that role.

## Status
Active
