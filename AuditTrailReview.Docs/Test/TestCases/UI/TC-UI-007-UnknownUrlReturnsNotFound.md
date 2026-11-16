# TC-UI-007 – Unknown URL Fails Cleanly, Not With a Crash

## Objective
Verify that navigating to a URL that matches no page returns HTTP 404 together with the application's friendly "Page not found" page (not an unhandled-exception page, and no stack trace or environment text) — a cheap routing/robustness smoke check that needs no login and no data setup.

## Preconditions
- `AuditTrailReview.WebApp` is running and reachable at `TestConfig.BaseUrl`.

## Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to `TestConfig.BaseUrl + "/this-page-does-not-exist-12345"` | Request completes |
| 2 | Read the HTTP response status | Status is `404` |
| 3 | Read the response body | Body is the application's "Page not found" page: heading, a *Reference* code, and a link to sign-in (anonymous) or the Dashboard (signed in); it contains no stack trace (e.g. no `System.Exception` / `at AuditTrailReview.WebApp.` text) and no "Development Mode" text |

## Assertions
- The navigation response status equals `404`.
- The response body contains the heading "Page not found" and a Reference code.
- The response body contains no stack-trace markers (`System.Exception`, `   at AuditTrailReview.WebApp.`) and no "Development Mode" text.

## Related Code
- Location: `AuditTrailReview.UIPlaywrightTest/SmokeTests.cs`
- Test method: `UnknownUrlReturnsNotFoundWithoutCrashing`
- Config: `TestConfig.BaseUrl`
- Relevant production wiring: `AuditTrailReview.WebApp/Program.cs` configures `app.UseExceptionHandler("/Error")` and `app.UseStatusCodePagesWithReExecute("/Error", "?statusCode={0}")`, so a missing route returns 404 with the friendly page from `Pages/Error.cshtml`
- Unit-level coverage of the page's content and status handling: TC-809
- Checked by hand on 2026-11-05 against the WebApp on the SQLite configuration: anonymous and signed-in `GET /no/such/page` return 404 with the page, a bad form post returns 400 with "Request not understood", and a forced exception returns 500 with "Something went wrong"

## Status
Active
