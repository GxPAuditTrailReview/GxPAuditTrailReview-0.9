# TC-816 – Every WebApp Page Declares Authorization

## Objective
Verify, as a regression guard for the `AuthorizeFolder("/")` convention in the WebApp's `Program.cs`, that every Razor Page model under `Pages/` carries either `[Authorize]` or an explicit `[AllowAnonymous]`, so a page added later cannot become reachable anonymously by omission.

## Preconditions
- Reflection over the compiled WebApp assembly. No host or database is needed. `Areas/Identity/Pages` is excluded: the default Identity UI governs it (and most of it is disabled, see `DisabledIdentityPagesTests`).

## Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Find every non-abstract `PageModel` type in a namespace starting with `AuditTrailReview.WebApp.Pages` | More than 10 types are found (otherwise the discovery filter is wrong) |
| 2 | Keep the types with neither `[Authorize]` nor `[AllowAnonymous]` (inherited attributes count) | The list is empty |

## Assertions
- The failure message names each undecorated page model, so the developer knows which page to fix.

## Related Code
- Test classes: `AuditTrailReview.UnitTest.Security.PageAuthorizationConventionTests`
- Methods: `AllPagesUnderPagesFolder_WhenInspected_DeclareAuthorizeOrAllowAnonymous`
- Production code: every PageModel under `AuditTrailReview.WebApp/Pages`; `Program.cs` (`AuthorizeFolder("/")`)
- Related: TC-810 (role restriction of the Log pages), TC-815 (security headers)

## Notes
- The test checks that a decision was made, not that it is right. Role checks per page are covered where a TC does it (TC-810) or by the page's own tests.
- Not covered: the runtime convention itself. An anonymous request to a protected URL is covered by TC-UI-001 and TC-UI-004.

## Status
Active
