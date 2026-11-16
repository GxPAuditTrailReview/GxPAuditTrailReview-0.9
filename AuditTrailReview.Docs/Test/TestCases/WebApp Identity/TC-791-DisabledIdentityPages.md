# TC-791 – WebApp Identity: Unused Identity Pages Disabled and Manage Pages Require Sign-In

## Objective
Verify that `DisabledIdentityPages.Apply` (`AuditTrailReview.WebApp/Security/DisabledIdentityPages.cs`, called from `Program.cs`'s `AddRazorPages`) switches off the scaffolded ASP.NET Core Identity pages the application doesn't use, so they answer 404, and that every page under `/Account/Manage` requires a signed-in user. The pages it closes include self-registration (`/Identity/Account/Register`): before this fix an anonymous visitor could open it, create an account and be signed in immediately, because `AuthorizeFolder("/")` covers only `Pages/`, not `Areas/Identity/Pages`. Pages the application does use (Login, Logout, password reset, Profile, Change password, and the 2FA sign-in and turn-off pages) must stay reachable.

## Preconditions
- The conventions are built the way `Program.cs` builds them: a `ServiceCollection` with `AddRazorPages(options => DisabledIdentityPages.Apply(options.Conventions))`, then `IOptions<RazorPagesOptions>.Value.Conventions`. The folder-authorization convention reads `MvcOptions` from DI, so a bare `new PageConventionCollection()` isn't enough.
- Each convention is applied to a real `PageApplicationModel` built from a `PageActionDescriptor` with `AreaName = "Identity"` and the page's `ViewEnginePath`, the same object Razor Pages builds at startup. No running host or database is needed.

## Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Apply the conventions to a disabled page: Register, RegisterConfirmation, ExternalLogin, Manage/Email, Manage/DeletePersonalData, Manage/DownloadPersonalData, Manage/EnableAuthenticator | The page model's filters contain `DisabledIdentityPages.NotFoundFilter` |
| 2 | Apply the conventions to a page in use: Login, Logout, AccessDenied, Lockout, ForgotPassword, ForgotPasswordConfirmation, ResetPassword, ResetPasswordConfirmation, LoginWith2fa, LoginWithRecoveryCode, Manage/Index, Manage/ChangePassword, Manage/Disable2fa | No `NotFoundFilter` is added |
| 3 | Apply the conventions to Manage/Index, Manage/ChangePassword, Manage/Disable2fa | The page requires authorization (`IAuthorizeData` endpoint metadata or an `AuthorizeFilter`) |
| 4 | Apply the conventions to Login, ForgotPassword, ResetPassword | The page does not require authorization (it must work before sign-in) |
| 5 | Apply the conventions to a page with the path `/Account/Register` outside the Identity area | No `NotFoundFilter` is added (the convention is scoped to the Identity area) |
| 6 | For every entry in `DisabledIdentityPages.Paths`, look up the matching scaffolded page model type in the WebApp assembly | Every path has a page model, so a typo can't silently leave a page reachable |
| 7 | Call `NotFoundFilter.OnResourceExecuting` | `context.Result` is a `NotFoundResult` |

## Assertions
- Self-registration and the other unused Identity pages (account self-deletion, e-mail self-change, external logins, 2FA setup, personal-data download) answer 404 instead of working.
- The sign-in, sign-out and password-reset flow is unaffected.
- `/Account/Manage/*` pages require a signed-in user by convention, not only by the scaffolded page code's own null-user check.
- The 404 is applied by page path, not by deleting the scaffolded files, so it also covers the copy of each page that AddDefaultIdentity's built-in UI would otherwise serve.

## Related Code
- Test classes: `AuditTrailReview.UnitTest.Security.DisabledIdentityPagesTests`
- Methods: `Apply_DisabledPage_AddsNotFoundFilter` (7 data rows), `Apply_PageInUse_DoesNotAddNotFoundFilter` (13 data rows), `Apply_ManagePage_RequiresAuthorization` (3 data rows), `Apply_AnonymousAccountPage_DoesNotRequireAuthorization` (3 data rows), `Apply_PageWithSamePathInAnotherArea_DoesNotAddNotFoundFilter`, `Paths_EachEntry_MatchesAScaffoldedPageModel`, `NotFoundFilter_OnResourceExecuting_SetsNotFoundResult`
- Production code: `AuditTrailReview.WebApp.Security.DisabledIdentityPages`, `AuditTrailReview.WebApp.Program` (`AddRazorPages` options), `Areas/Identity/Pages/Account/Manage/_ManageNav.cshtml` (menu reduced to Profile and Password), `Areas/Identity/Pages/Account/Manage/Index.cshtml(.cs)` (link to turn 2FA off when it is on), `Areas/Identity/Pages/Account/Manage/Disable2fa.cshtml(.cs)` (redirects to Profile)
- Related: TC-790 (another Identity page gate), TC-UI-001 / TC-UI-004 (anonymous access to protected pages)

## Notes
- The unit tests check the conventions, not the HTTP pipeline. The pipeline was checked by hand on 2026-11-05 against the WebApp running on the SQLite configuration: anonymous GET and a POST with a valid antiforgery token to `/Identity/Account/Register` both returned 404 and no account was created. Login, ForgotPassword, Lockout and AccessDenied returned 200. Anonymous requests to `/Identity/Account/Manage/*` redirected to Login. Signed in as the seeded admin, Manage/Index and Manage/ChangePassword returned 200, Manage/Email, DeletePersonalData, PersonalData and TwoFactorAuthentication returned 404, and the menu showed only Profile and Password.
- Not covered by an automated test: the Profile page's "turn it off" link for a user who already has 2FA turned on, and the Disable2fa redirect back to Profile.
- A Playwright check that `/Identity/Account/Register` returns 404 would cover this end to end; it isn't automated yet.

## Status
Active
