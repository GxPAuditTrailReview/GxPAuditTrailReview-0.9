# TC-809 – Error Page Content and Request Correlation

## Objective
Verify that the shared Error page (`Pages/Error.cshtml.cs`, the target of both `UseExceptionHandler("/Error")` and `UseStatusCodePagesWithReExecute("/Error", "?statusCode={0}")` in `Program.cs`) shows a friendly, status-specific message with a reference code and never leaks environment or exception details, and that `RequestCorrelationMiddleware` tags every log event written during a request with that same reference as `CorrelationId`, so support can find the matching `dbo.ApplicationLog` rows.

## Preconditions
- `ErrorModel` is constructed with a `DefaultHttpContext` whose `TraceIdentifier` is a known value, set through a `PageContext`.
- `RequestCorrelationMiddleware` is exercised with a real Serilog `Logger` (`Enrich.FromLogContext()`) writing to an in-memory sink, so the log properties are inspected on real `LogEvent` objects. No host or database is needed.

## Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | `ErrorModel.OnGet(null)` | `RequestId` equals `HttpContext.TraceIdentifier` and `ShowRequestId` is true |
| 2 | `OnGet` with status 404, 403, 400, 413, 500, 503, 418 | Heading is "Page not found", "Access denied", "Request not understood", "File too large", "Something went wrong" (for both 500 and 503) and "Request could not be completed"; message and icon are never empty |
| 3 | `OnGet(404)` on a response whose status was reset to 200 | The response status is set back to 404 |
| 4 | `OnGet(null)` with the response status already 500 (the exception handler's case) | Status 500, heading "Something went wrong" |
| 5 | `OnGet` with null, 0, 200, 302, 600 and -5, response status 200 | Falls back to the generic server-error text, and the response status stays 200 (opening `/Error` directly doesn't turn into an error status) |
| 6 | `OnPost(400)` | Same content as `OnGet` (a failed form post is re-executed with its original method, which would otherwise render an empty page) |
| 7 | `Describe(500)` | The message contains none of "Development", "ASPNETCORE" or "exception" |
| 8 | Run `RequestCorrelationMiddleware` and log inside the request | The event has `CorrelationId` = `TraceIdentifier` |
| 9 | Log after the middleware has finished | The event has no `CorrelationId` |
| 10 | Inside the request, push an inner `CorrelationId` ("42") and log, then log again after the inner scope | The first event carries "42" (a scan upload's own id wins), the second carries the `TraceIdentifier` |
| 11 | The next delegate throws | The exception propagates, and a later log event has no `CorrelationId` (the scope is released) |

## Assertions
- The text shown for a server error says nothing about the environment, configuration or exception.
- The reference on the page is the same value the log events carry, which is what makes the support lookup in the Technical Manual work.
- An inner `CorrelationId` still overrides for its own events, so the existing "trace one upload by scan id" lookup keeps working.

## Related Code
- Test classes: `AuditTrailReview.UnitTest.WebApp.Pages.ErrorModelTests`, `AuditTrailReview.UnitTest.Security.RequestCorrelationMiddlewareTests`
- Methods: `OnGet_Always_ExposesTraceIdentifierAsReference`, `OnGet_StatusCodeQuery_ShowsMatchingHeading` (7 data rows), `OnGet_StatusCodeQuery_SetsResponseStatusToMatch`, `OnGet_NoQueryAndExceptionHandlerStatus_UsesResponseStatus`, `OnGet_OutOfRangeOrMissingStatus_FallsBackToGenericServerError` (6 data rows), `OnPost_FailedFormPost_ShowsSameContentAsGet`, `Describe_ServerError_DoesNotMentionEnvironmentOrExceptions`, `InvokeAsync_LogInsideRequest_CarriesTraceIdentifierAsCorrelationId`, `InvokeAsync_LogAfterRequestCompletes_DoesNotCarryCorrelationId`, `InvokeAsync_InnerCorrelationId_OverridesForItsOwnEventsOnly`, `InvokeAsync_NextThrows_ExceptionPropagatesAndScopeIsReleased`
- Production code: `AuditTrailReview.WebApp.Pages.ErrorModel` (`Pages/Error.cshtml`), `AuditTrailReview.WebApp.Security.RequestCorrelationMiddleware`, `Areas/Identity/Pages/Account/AccessDenied.cshtml` (same look, for a 403 from the cookie handler), `Program.cs` (`UseStatusCodePagesWithReExecute`, `UseMiddleware<RequestCorrelationMiddleware>`)
- Related: TC-UI-007 (end-to-end 404), TC-801 (the audit-trail log is separate from `dbo.ApplicationLog`)

## Notes
- The rendered Razor views (`Error.cshtml`, `AccessDenied.cshtml`) and the real pipeline wiring are not covered by an automated test. They were checked by hand on 2026-11-05 against the WebApp on the SQLite configuration: unknown URL, bad form post and a forced exception return 404, 400 and 500 with the matching pages, and the link goes to sign-in when anonymous and to the Dashboard when signed in.
- Whether the `CorrelationId` column is filled in `dbo.ApplicationLog` itself is only exercised against SQL Server (the SQLite flavor doesn't use that sink). The unit tests check the log property, not the sink.
- A real 403 (a signed-in user without the role) goes through the cookie handler to `AccessDenied`; it wasn't exercised with a non-admin account.

## Status
Active
