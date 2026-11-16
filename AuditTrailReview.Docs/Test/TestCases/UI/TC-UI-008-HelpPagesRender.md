# TC-UI-008 – Help and User Manual Pages Render

## Objective
Verify the static, docs-corpus-backed Help pages render for a logged-in user, exercising the `DocsCorpus` wiring (ADR/CLAUDE.md "Docs are also runtime data") keeping this test free of any AI network dependency.

## Preconditions
- `AuditTrailReview.WebApp` is running and reachable at `TestConfig.BaseUrl`, built (not just run) so `DocsCorpus/` was copied into its output, per the allowlisted `Content Include` entries in `AuditTrailReview.WebApp.csproj`.
- `TestConfig.LoginEmail` / `TestConfig.LoginPassword` are set to a valid account.

## Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Log in via `/Identity/Account/Login` | Redirected to `/Dashboard` |
| 2 | Navigate to `/Help/Index` | Page loads; breadcrumb shows `Help` |
| 3 | Read the page heading | Contains `Getting started with GxPAuditTrailReview` |
| 4 | Navigate to `/Help/UserManual` | Page loads; breadcrumb shows `Help` → `User manual` |
| 5 | Read the page content | The "manual couldn't be found" warning (`.alert-warning`) is **not** shown, i.e. the user manual content rendered |

## Assertions
- `/Help/Index` loads with breadcrumb text `Help` and heading text containing `Getting started with GxPAuditTrailReview`.
- `/Help/UserManual` loads with breadcrumb `Help` / `User manual`, and its `.alert-warning` "manual couldn't be found" element is not present/visible.

## Related Code
- Location: `AuditTrailReview.UIPlaywrightTest/SmokeTests.cs`
- Test method: `HelpAndUserManualPagesRender`
- Config: `TestConfig.BaseUrl`, `LoginEmail`, `LoginPassword`
- Production code: `AuditTrailReview.WebApp.Pages.Help.IndexModel`, `AuditTrailReview.WebApp.Pages.Help.UserManualModel`

## Notes
- The former "ask the docs" chatbot (`/Help/Chat`) was removed before v1.0; this test now covers the whole Help area (FR-040).

## Status
Active
