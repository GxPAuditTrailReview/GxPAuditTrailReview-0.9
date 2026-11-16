# TC-UI-004 – Logout Ends the Session and Re-Guards Protected Pages

## Objective
Verify that logging out actually clears the authentication cookie — not just the visible "Logout" control — by confirming a protected page is no longer reachable afterwards.

## Preconditions
- `AuditTrailReview.WebApp` is running and reachable at `TestConfig.BaseUrl`.
- `TestConfig.LoginEmail` / `TestConfig.LoginPassword` are set to a valid account.

## Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Log in via `/Identity/Account/Login` (fill `#Input_Email`/`#Input_Password`, click `#login-submit`) | Redirected to `/Dashboard` |
| 2 | Submit the `#logoutForm` form (its `#logout` button, in the navbar) | Form posts to `/Identity/Account/Logout` |
| 3 | Check current URL | Browser is on an anonymous page (Login or Home), not `/Dashboard` |
| 4 | Navigate directly to `/Dashboard/Index` | Browser is redirected to `/Identity/Account/Login`, the Dashboard content is **not** rendered |

## Assertions
- After step 2, `page.Url` no longer contains `/Dashboard`.
- After step 4, `page.Url` contains `/Identity/Account/Login` (the auth cookie was actually cleared, not just the nav control hidden).

## Related Code
- Location: `AuditTrailReview.UIPlaywrightTest/SmokeTests.cs`
- Test method: `LogoutEndsSessionAndReGuardsProtectedPages`
- Config: `TestConfig.BaseUrl`, `LoginEmail`, `LoginPassword`

## Page Elements Used
| Element | Selector | File |
|---------|----------|------|
| Email input | `#Input_Email` | `Pages/Account/Login.cshtml` |
| Password input | `#Input_Password` | `Pages/Account/Login.cshtml` |
| Submit button | `#login-submit` | `Pages/Account/Login.cshtml` |
| Logout form | `#logoutForm` (`asp-page="/Account/Logout"`) | `Pages/Shared/_LoginPartial.cshtml` |
| Logout button | `#logout` | `Pages/Shared/_LoginPartial.cshtml` |

## Status
Active
