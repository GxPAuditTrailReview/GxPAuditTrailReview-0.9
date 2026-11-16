# TC-811 – Application Services: Session Timeout Cookie Policy

## Objective
Verify the session policy `SecurityCookieConfigureOptions` (`Services/SecurityCookieSetup.cs`) applies to the Identity application cookie: the idle timeout with sliding expiration, the absolute maximum session length, background polling not renewing the cookie, live reload when the Security tab is saved, and the 1-minute security-stamp validation interval (`SecurityStampValidatorConfigureOptions`) that lets a password change, role change or deactivation end other sessions quickly (Annex 11 §12, Part 11 controls for open sessions; Path to v1.0 Cybersecurity item 4).

## Preconditions
- Tests build real `CookieAuthenticationOptions`, `AuthenticationProperties`, `DefaultHttpContext` and `SecurityOptionsState` instances; no database or host is started.
- Values (`IdleTimeoutMinutes`, `AbsoluteSessionHours`) come from `SecuritySettings`, edited on the Security tab (TC-703).

## Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | `Configure` for the `Identity.Application` scheme with `IdleTimeoutMinutes = 25` | `ExpireTimeSpan` is 25 minutes and `SlidingExpiration` is true |
| 2 | `Configure` for another scheme (`Identity.External`) | Options are left at their defaults |
| 3 | `SecurityOptionsState.Reload` with a new idle timeout after capturing the change token | The token reports a change (name `Identity.Application`) and `Configure` now yields the new timeout |
| 4 | `OnSigningIn` for a new sign-in | The session start time is stamped into the ticket's `AuthenticationProperties` |
| 5 | `StampSessionStart` on a ticket that is already stamped | The original start time is kept |
| 6 | `IsPastAbsoluteMaximum` with a start 9 h ago and a 10 h maximum | `false` |
| 7 | `IsPastAbsoluteMaximum` with a start 11 h ago and a 10 h maximum | `true` |
| 8 | `IsPastAbsoluteMaximum` with no start stamp | `true` (tickets without a stamp are rejected) |
| 9 | `OnCheckSlidingExpiration` on a request carrying the `X-Background-Poll` header | `ShouldRenew` is false |
| 10 | `OnCheckSlidingExpiration` on a normal request | `ShouldRenew` stays true |
| 11 | `SecurityStampValidatorConfigureOptions.Configure` | `ValidationInterval` is 1 minute |

## Assertions
- The idle timeout is sliding and comes from `SecuritySettings`; a Security tab save reaches the cookie options without an application restart.
- A session is rejected once it is older than the absolute maximum, however active it has been, and a ticket without a start stamp is never accepted.
- Background polling (the notification bell) cannot keep an idle session alive.
- The security stamp is re-validated every minute, so the existing `UpdateSecurityStampAsync` calls (deactivation, lockout escalation, password change, role change) take effect within about a minute.

## Related Code
- Test classes: `AuditTrailReview.UnitTest.WebApp.Services.SecurityCookieSetupTests`
- Methods: `Configure_IdentityApplicationScheme_AppliesIdleTimeoutWithSlidingExpiration`, `Configure_OtherScheme_LeavesOptionsUntouched`, `Configure_AfterReload_UsesNewIdleTimeout`, `OnSigningIn_NewSession_StampsSessionStart`, `StampSessionStart_AlreadyStamped_KeepsOriginalStart`, `IsPastAbsoluteMaximum_WithinMaximum_ReturnsFalse`, `IsPastAbsoluteMaximum_OlderThanMaximum_ReturnsTrue`, `IsPastAbsoluteMaximum_MissingStamp_ReturnsTrue`, `OnCheckSlidingExpiration_BackgroundPoll_DoesNotRenew`, `OnCheckSlidingExpiration_NormalRequest_Renews`, `SecurityStampValidatorConfigureOptions_Configure_SetsOneMinuteInterval`
- Production code: `AuditTrailReview.WebApp.Services.SecurityCookieConfigureOptions`, `SecurityCookieOptionsChangeTokenSource`, `SecurityStampValidatorConfigureOptions` (`Services/SecurityCookieSetup.cs`), `AuditTrailReview.Model.SecuritySettings` (`IdleTimeoutMinutes`, `AbsoluteSessionHours`, `SessionWarningMinutes`), `wwwroot/js/session-timeout.js`
- Requirement: CT-06 (Security Settings), per the IQOQ protocol
- Related: TC-703 (Security tab save/validation of the session settings), TC-805 (security stamp update on deactivation)

## Notes
Not covered by automated tests, and not yet verified manually: `OnValidatePrincipal` end to end (rejecting and signing out a ticket past the maximum, composed with Identity's stamp validator), that a role change rotates the security stamp (`AdminPanel/User/Edit`), the `/Session/KeepAlive` endpoint, removal of "Remember me" from the login pages, and the browser warning dialog and automatic sign-out in `session-timeout.js`. These need a running host and browser.

## Status
Active
