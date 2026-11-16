# TC-UI-001 – Anonymous Visitor Is Redirected to Login

## Objective
Verify that the WebApp is up and that an unauthenticated visitor hitting the root URL is routed to the Login page rather than any protected content — a basic liveness and anonymous-routing smoke check with no external dependencies.

## Preconditions
- `AuditTrailReview.WebApp` is running and reachable at `TestConfig.BaseUrl`.
- No prior session/auth cookie for the browser context (a fresh `IBrowserContext`, as created per test in `MenuLinkCrawlerTests.TestInitializeAsync`).

## Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to `TestConfig.BaseUrl` (`/`) with no prior session | Request succeeds (no 5xx); browser ends up at `/Identity/Account/Login` |
| 2 | Read the page content | The login form is visible: email input, password input, and a submit button |
| 3 | Check current URL | URL contains `/Identity/Account/Login` |

## Assertions
- `page.Url` contains `/Identity/Account/Login` after navigating to `/`.
- `#Input_Email`, `#Input_Password`, and `#login-submit` are all visible.

## Related Code
- Location: `AuditTrailReview.UIPlaywrightTest/SmokeTests.cs`
- Test method: `AnonymousVisitorIsRedirectedToLogin`
- Config: `TestConfig.BaseUrl`

## Page Elements Used
| Element | Selector |
|---------|----------|
| Email input | `#Input_Email` |
| Password input | `#Input_Password` |
| Submit button | `#login-submit` |

## Status
Active
