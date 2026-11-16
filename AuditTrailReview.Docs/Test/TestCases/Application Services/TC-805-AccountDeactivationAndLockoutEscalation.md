# TC-805 – Application Services: Account Deactivation Status and Lockout Escalation

## Objective
Verify `UserAccountStatus.IsDeactivated` (`Helpers/UserAccountStatus.cs`), the shared check used by AdminPanel/User's Edit/Index pages (TC-702) and the Login page to tell whether an account is deactivated, and `FailedLoginLockoutService` (`Services/FailedLoginLockoutService.cs`), which escalates an account that just hit Identity's failed-login lockout threshold (`SecuritySettings.LockoutMaxFailedAccessAttempts`) into that same indefinite deactivation -- except for the sole remaining active Admin, who keeps Identity's normal short auto-unlock instead so the app can never be completely locked out of its own admin panel.

## Preconditions
- `UserAccountStatusTests` constructs plain `ApplicationUser` instances directly (no database) -- `IsDeactivated` is a pure function of `LockoutEnabled`/`LockoutEnd`.
- `FailedLoginLockoutServiceTests` constructs the service with a real `UserManager<ApplicationUser>` / `RoleManager<IdentityRole>` (via `PageModelTestHelpers`) and a real `AuditTrailService`, backed by an EF Core InMemory `ApplicationDbContext`.

## Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | `UserAccountStatus.IsDeactivated` with `LockoutEnabled=true` and `LockoutEnd` far in the future | Returns `true` |
| 2 | `UserAccountStatus.IsDeactivated` with `LockoutEnabled=true` and `LockoutEnd` in the past | Returns `false` |
| 3 | `UserAccountStatus.IsDeactivated` with `LockoutEnabled=false` (even with a future `LockoutEnd`) | Returns `false` |
| 4 | `UserAccountStatus.IsDeactivated` with `LockoutEnd` left `null` | Returns `false` |
| 5 | `FailedLoginLockoutService.EscalateToDeactivatedAsync` for a non-Admin user | Sets `LockoutEnabled=true`/`LockoutEnd=DateTimeOffset.MaxValue`, calls `UpdateSecurityStampAsync`, and returns `true` |
| 6 | `FailedLoginLockoutService.EscalateToDeactivatedAsync` for the sole active Admin | Does **not** change `LockoutEnd` (Identity's own short auto-unlock stands) and returns `false` |
| 7 | `FailedLoginLockoutService.EscalateToDeactivatedAsync` for an Admin when another active Admin exists | Escalates (sets `LockoutEnd=DateTimeOffset.MaxValue`) and returns `true`, same as a non-Admin |
| 8 | `FailedLoginLockoutService.EscalateToDeactivatedAsync` when it escalates | Logs an audit "Update" entry with `AccountStatus` changing from `Active` to `Deactivated`, attributed to `"System"` and carrying the target user's Id in `TargetUserId` |
| 9 | `FailedLoginLockoutService.EscalateToDeactivatedAsync` when exempted (sole active Admin) | Logs no audit entry |

## Assertions
- `IsDeactivated` only reports `true` when both `LockoutEnabled` is set and `LockoutEnd` is in the future -- it is the single source of truth AdminPanel/User (TC-702) and Login.cshtml.cs's escalation path both read.
- Escalation always invalidates the target's security stamp when it deactivates, so a live session can't keep using the app after the threshold is crossed.
- The sole-active-Admin exemption is the only case where escalation is skipped; it applies even though the account did reach the failed-attempt threshold, and is the deliberate trade-off that keeps the app from ever being completely locked out of its own admin panel (see the production code's remarks).
- An exempted escalation attempt makes no database change beyond what Identity's own failed-attempt counting already did, and writes no audit entry.

## Related Code
- Test classes: `AuditTrailReview.UnitTest.WebApp.Helpers.UserAccountStatusTests`, `AuditTrailReview.UnitTest.WebApp.Services.FailedLoginLockoutServiceTests`
- Methods: `IsDeactivated_LockoutEndInFuture_ReturnsTrue`, `IsDeactivated_LockoutEndInPast_ReturnsFalse`, `IsDeactivated_LockoutEnabledFalse_ReturnsFalse`, `IsDeactivated_NoLockoutEnd_ReturnsFalse`, `EscalateToDeactivatedAsync_NonAdminUser_SetsIndefiniteLockoutAndUpdatesSecurityStamp`, `EscalateToDeactivatedAsync_SoleActiveAdmin_DoesNotEscalate`, `EscalateToDeactivatedAsync_AdminWithOtherActiveAdmin_Escalates`, `EscalateToDeactivatedAsync_Escalates_WritesAuditTrailEntryForTargetUser`, `EscalateToDeactivatedAsync_SoleActiveAdmin_DoesNotWriteAuditTrailEntry`
- Production code: `AuditTrailReview.WebApp.Helpers.UserAccountStatus`, `AuditTrailReview.WebApp.Services.FailedLoginLockoutService`, `AuditTrailReview.WebApp.Areas.Identity.Pages.Account.LoginModel` (calls this service from its `IsLockedOut` branch), `AuditTrailReview.Model.SecuritySettings.LockoutMaxFailedAccessAttempts` (the threshold, default 5)
- Related: TC-702 (the AdminPanel/User "Deactivate this account" checkbox that writes/reads the same state); TC-790 (the "Forgot your password?" gate that also reads `IsDeactivated`)

## Notes
Not covered here, and not yet verified any other way: the actual `PasswordSignInAsync`/`SignInManager` flow that calls into this service from `Login.cshtml.cs`'s `IsLockedOut` branch. Constructing a real `SignInManager` would also need HttpContext authentication services wired up for its internal `SignOutAsync` call on the lockout path, judged out of scope for a PageModel-style unit test (see the test class remarks). Manual verification for this feature exercised the AdminPanel/User checkbox deactivate/reactivate path and the resulting Lockout-page message end-to-end, but not the "5 failed password attempts" trigger itself -- that remains **manual, not yet automated or manually verified**.

## Status
Active
