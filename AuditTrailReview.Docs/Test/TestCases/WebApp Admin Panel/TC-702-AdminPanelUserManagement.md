# TC-702 – WebApp Admin Panel: User Management

## Objective
Verify the Admin Panel's User CRUD page models (`Pages/AdminPanel/User/{Index,Create,Edit,Details,Delete}.cshtml.cs`): listing/paging/searching users, creating a user (including role assignment, weak-password and duplicate-email rejection), editing a user's name/email/role, deactivating/reactivating a user's account without deleting it, viewing a user's details, and deleting a user, each with the correct audit-trail logging and redirect behavior.

## Preconditions
- Each page model is constructed with an EF Core InMemory `ApplicationDbContext` and real `UserManager<ApplicationUser>` / `RoleManager<IdentityRole>` instances (via `PageModelTestHelpers`), backed by the same context.
- Test passwords satisfy the framework-default Identity password policy (e.g. `"Aa1!aaaa"`), since the unit tests don't run through the application's own configured policy.

## Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | `IndexModel.OnGetAsync` with no users, with multiple users, and with more users than one page size | Empty list when there are none; all users returned ordered by user name; pagination honors the page size and clamps a page number beyond the last page |
| 2 | `IndexModel.OnGetAsync` with a search term matching user name, first name, last name, or email, and matching nothing | Search filters across all users on each field, not just the current page; a non-matching term returns an empty result |
| 3 | `CreateModel.OnGetAsync` | Populates the roles list from the database |
| 4 | `CreateModel.OnPostAsync` with an invalid model state | Returns the page without creating a user |
| 5 | `CreateModel.OnPostAsync` with a valid model, with a role selected, and with no role selected | Creates the user and redirects; the selected role is assigned when supplied, and no role is assigned when none is selected |
| 6 | `CreateModel.OnPostAsync` with a valid model | Logs an audit "Create" entry carrying the newly created user's Id in `TargetUserId` |
| 7 | `CreateModel.OnPostAsync` with a weak password, and with a duplicate email | Returns the page with model errors; no user is created in either case |
| 8 | `CreateModel.OnPostAsync` with a first/last name supplied | The name is persisted on the created user |
| 9 | `EditModel.OnGetAsync` with a null id, and with an unknown id | Returns `NotFound` |
| 10 | `EditModel.OnGetAsync` with an existing user | Populates the input model from the user's current data |
| 11 | `EditModel.OnPostAsync` with an unknown id | Returns `NotFound` |
| 12 | `EditModel.OnPostAsync` with a valid update | Updates email and user name; a changed first/last name updates those fields and logs an audit entry; an unchanged role leaves the user in the same role, while a changed role updates role membership |
| 13 | `EditModel.OnPostAsync` with a valid update | Logs an audit "Update" entry carrying the edited user's Id in `TargetUserId` and redirects to the Index page, even when the underlying `UpdateAsync` call itself reports failure |
| 14 | `EditModel.OnGetAsync` for an already-deactivated user | `Input.IsDeactivated` is pre-populated `true` so the checkbox reflects current state |
| 15 | `EditModel.OnPostAsync` checking the "Deactivate this account" checkbox | Sets `LockoutEnabled=true`/`LockoutEnd=DateTimeOffset.MaxValue` and calls `UpdateSecurityStampAsync` (so any live session is invalidated); logs an audit "Update" entry with `AccountStatus` changing from `Active` to `Deactivated` |
| 16 | `EditModel.OnPostAsync` unchecking the checkbox on an already-deactivated user | Clears `LockoutEnd` and resets `AccessFailedCount` to 0 |
| 17 | `EditModel.OnPostAsync` checking the checkbox while editing the signed-in admin's own account | Returns the page with a `ModelState` error ("You cannot deactivate your own account.") and leaves lockout state unchanged; no audit entry is logged |
| 18 | `EditModel.OnPostAsync` checking the checkbox for the only remaining active Admin | Returns the page with a `ModelState` error ("Cannot deactivate the only remaining active Admin.") and leaves lockout state unchanged |
| 19 | `EditModel.OnPostAsync` checking the checkbox for an Admin when another active Admin still exists | Succeeds and redirects |
| 20 | `IndexModel.OnGetAsync` with one deactivated user and one active user on the page | `DeactivatedUserIds` contains only the deactivated user's Id, driving the Status badge |
| 21 | `DetailsModel.OnGetAsync` with a null id, with an unknown id, and with an existing user | `NotFound` for the first two; populates the user for the third |
| 22 | `DetailsModel.OnGetAsync` for a user's own "Audit trail" tab, with an entry for this user and one for a different user | Only the entry whose `TargetUserId` matches the viewed user is included; the other user's entry is excluded |
| 23 | `DetailsModel.OnGetAsync` with `AuditTrailSearchTerm` set | Filters the user's audit entries to those matching the term |
| 24 | `DetailsModel.OnGetAsync` with more audit entries than one page size, requesting page 2 | Pagination honors `AuditTrailPageSize` and returns the correct `AuditTrailTotalPages`/remaining entries |
| 25 | `DeleteModel.OnGetAsync` with a null id, with an unknown id, and with an existing user | `NotFound` for the first two; populates the user for the third |
| 26 | `DeleteModel.OnPostAsync` with a null id or an unknown id | Returns `NotFound` |
| 27 | `DeleteModel.OnPostAsync` with an existing user | Deletes the user, logs an audit "Delete" entry carrying the deleted user's Id in `TargetUserId`, and redirects to the Index page |
| 28 | `DeleteModel.OnGetAsync` and `OnPostAsync` for a user who has uploaded or queued a scan | Redirects to the Index page without showing the page, and does not delete the user (it would orphan "Uploaded by") |
| 29 | `IndexModel.OnGetAsync` with one user who has uploaded a scan | Only that user's id is flagged, so the Delete button is greyed out for them |
| 30 | `UserScanActivityService.HasUploadedScansAsync` for a user with a `Scan` row, with a `ScanUploadJob` row, and with neither | True, true, false |
| 31 | `UserScanActivityService.GetUserIdsWithScanActivityAsync` for a mix of users | Returns only the ids with a scan or a queued job (the batched form used by the Index page) |

## Assertions
- Every handler that takes an id guards against `null` and unknown ids with `NotFound` before touching the database.
- Weak passwords and duplicate emails are rejected via `ModelState` errors with no user created.
- Create/Edit/Delete log an audit-trail entry only for actual changes, attributed to the signed-in user, and every such entry's `TargetUserId` identifies the account that was created, edited, or deleted; role assignment on create/edit is correct in all three cases (assigned, left unassigned, changed).
- The Details page's "Audit trail" tab is scoped strictly to entries whose `TargetUserId` matches the account being viewed, and supports search and paging.
- A user who has uploaded or queued a scan cannot be deleted, in the UI or on the server; the same check drives the greyed-out Delete button.
- Deactivating an account is reversible (reactivation clears `LockoutEnd` and resets `AccessFailedCount`, not a delete), is blocked for the signed-in admin's own account and for the only remaining active Admin, and always invalidates the target's security stamp so a live session can't keep using the app after deactivation. The Index page's Status badge reflects the same `LockoutEnd`/`LockoutEnabled` state the checkbox writes.

## Related Code
- Test classes: `AuditTrailReview.UnitTest.WebApp.AdminPanel.User.{IndexModelTests, CreateModelTests, EditModelTests, DetailsModelTests, DeleteModelTests}`
- Methods: all methods in `WebApp/AdminPanel/User/IndexModelTests.cs`, `CreateModelTests.cs`, `EditModelTests.cs`, `DetailsModelTests.cs`, `DeleteModelTests.cs` -- including `OnPostAsync_ValidModel_AuditEntryRecordsCreatedTargetUserId`, `OnPostAsync_ExistingUser_AuditEntryRecordsDeletedTargetUserId`, `OnGetAsync_UserWithScanUpload_RedirectsToIndexWithoutShowingPage`, `OnPostAsync_UserWithScanUpload_DoesNotDeleteAndRedirectsToIndex`, `OnGetAsync_OneUserHasUploadedAScan_OnlyThatUserIdIsFlagged`; and from `Engine/UserScanActivityServiceTests.cs` (`AuditTrailReview.UnitTest.Engine.UserScanActivityServiceTests`): `HasUploadedScansAsync_UserWithScanRow_ReturnsTrue`, `HasUploadedScansAsync_UserWithScanUploadJobRow_ReturnsTrue`, `HasUploadedScansAsync_UserWithNoRows_ReturnsFalse`, `GetUserIdsWithScanActivityAsync_MixOfUsers_ReturnsOnlyThoseWithActivity`, `OnPostAsync_ValidUpdate_AuditEntryRecordsEditedTargetUserId`, the `DetailsModelTests` Audit trail tab methods (`OnGetAsync_AuditTrailEntriesForThisUser_Included`, `OnGetAsync_AuditTrailEntriesFromOtherUser_NotIncluded`, `OnGetAsync_AuditTrailSearchTerm_FiltersToMatchingEntries`, `OnGetAsync_AuditTrailPaging_ReturnsRequestedPage`), and the deactivate/reactivate methods `OnPostAsync_CheckDeactivate_SetsLockoutEndFarFutureAndUpdatesSecurityStamp`, `OnPostAsync_UncheckDeactivate_ClearsLockoutEndAndResetsAccessFailedCount`, `OnPostAsync_DeactivateOwnAccount_ReturnsPageWithErrorAndLeavesLockoutUnchanged`, `OnPostAsync_DeactivateOnlyRemainingAdmin_ReturnsPageWithErrorAndLeavesLockoutUnchanged`, `OnPostAsync_DeactivateAdminWithOtherActiveAdmin_Succeeds`, `OnPostAsync_DeactivateUser_LogsAuditEntryWithAccountStatusChange`, `OnGetAsync_DeactivatedUser_PopulatesIsDeactivatedCheckbox` (`EditModelTests`), and `OnGetAsync_DeactivatedUser_IsFlaggedInactiveInResult` (`IndexModelTests`)
- Production code: `AuditTrailReview.WebApp.Pages.AdminPanel.User.{IndexModel, CreateModel, EditModel, DetailsModel, DeleteModel}`, `AuditTrailReview.WebApp.Helpers.UserAccountStatus`, `AuditTrailReview.Engine.Services.UserScanActivityService`
- Related: TC-801 (the `TargetUserId` column written by `AuditTrailService`); TC-805 (the shared `UserAccountStatus.IsDeactivated` check and the auto-deactivation-on-repeated-failed-logins path that puts an account into this same state from the Login page instead of AdminPanel); TC-790 (the deactivated-account gate on the "Forgot your password?" flow)

## Notes
Whether a deactivated account is actually refused sign-in (Identity's `PreSignInCheck`/`IsLockedOutAsync`, which this page's tests don't exercise since they call the page handlers directly, not through `SignInManager`) is covered by TC-805's `FailedLoginLockoutServiceTests` only indirectly -- there is no dedicated unit test asserting `PasswordSignInAsync` itself returns `LockedOut` for a deactivated user. That behavior, and the updated Lockout-page message, were confirmed by manual verification instead; the existing UI test TC-604 covers invalid-credential login in general but not the deactivated-account case specifically.

## Status
Active
