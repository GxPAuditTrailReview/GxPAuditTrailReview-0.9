# TC-802 – Application Services: User Activity Logging

## Objective
Verify `UserActivityLogService.LogAsync`, the service that records security-relevant user activity (e.g. login attempts) including the caller's IP address and user agent, derived from the current `HttpContext`.

## Preconditions
- No web server involved: `UserActivityLogService` is constructed directly against an EF Core InMemory `ApplicationDbContext`; a minimal `DefaultHttpContext` stands in for the real request, with its remote IP, `User-Agent` header, and optional `X-Forwarded-For` header set per test.

## Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call `LogAsync` with a valid context | Exactly one entry is saved |
| 2 | Inspect the saved entry's `Action`, `UserId`, `UserName`, `Details`, and `Success` fields after a call that supplies each | Each field is persisted as supplied |
| 3 | Call `LogAsync` with `null` details, `null` user id, and `null` user name | Each persists as `null` rather than throwing or defaulting to an empty string |
| 4 | Call `LogAsync` with `Success` true and with `Success` false | Both values are persisted as given |
| 5 | Call `LogAsync` twice | Both entries are saved independently |
| 6 | Inspect the `Timestamp` field | Always UTC |
| 7 | Call `LogAsync` with a normal remote IP, with a loopback remote IP, with a `null` remote IP, and with an IPv4-mapped-to-IPv6 address | The IP is persisted as given; loopback renders as "localhost"; `null` renders as "Unknown"; an IPv4-mapped IPv6 address is normalized to its IPv4 form |
| 8 | Call `LogAsync` with an `X-Forwarded-For` header holding one address, holding multiple comma-separated addresses, and holding an invalid address | The header is ignored in every case: the connection's remote IP is recorded. Through `ForwardedHeadersSetup` + the middleware, a forged header from an untrusted sender is ignored (and `X-Forwarded-Proto` is not honoured), while one from a configured `KnownProxies` entry yields the client IP; invalid `KnownNetworks` values are rejected |
| 9 | Call `LogAsync` with and without a `User-Agent` header | The header value is persisted when present; its absence persists as an empty string rather than `null` |

## Assertions
- All supplied fields (action, user id, user name, details, success) are persisted exactly as given, including `null` where applicable.
- IP address resolution records only the connection address (after `UseForwardedHeaders`, restricted to the configured trusted proxies), never the raw `X-Forwarded-For` header, with loopback and IPv4-mapped-IPv6 normalization applied.
- Timestamps are always UTC.

## Related Code
- Test class: `AuditTrailReview.UnitTest.UserActivityLogServiceTests`
- Methods: all `LogAsync_*` methods
- Production code: `AuditTrailReview.Engine.Services.UserActivityLogService.LogAsync`
- Requirement: NFR-042 (UTC timestamps)

## Status
Active
