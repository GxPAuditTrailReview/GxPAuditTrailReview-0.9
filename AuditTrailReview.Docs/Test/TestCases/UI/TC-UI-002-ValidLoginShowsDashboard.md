# TC-UI-002 – Valid Login Reaches Dashboard With Summary Cards

## Objective
Verify that the seeded account can log in through the login form and is redirected to a Dashboard that actually renders its summary data, not just an empty shell.

## Preconditions
- `AuditTrailReview.WebApp` is running and reachable at `TestConfig.BaseUrl`.
- `TestConfig.LoginEmail` / `TestConfig.LoginPassword` are set to a valid account (defaults to the seeded Admin account, see `TestConfig.cs`).

## Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to `/Identity/Account/Login` | Login form is displayed |
| 2 | Fill `#Input_Email` with `TestConfig.LoginEmail` | Field is populated |
| 3 | Fill `#Input_Password` with `TestConfig.LoginPassword` | Field is populated |
| 4 | Click `#login-submit` | Form submits |
| 5 | Wait for the URL to contain `/Dashboard` | Dashboard page loads |
| 6 | Read the breadcrumb | `ol.breadcrumb li.breadcrumb-item.active` text equals `Dashboard` |
| 7 | Read the four summary cards | `.card.bg-primary .display-4`, `.card.bg-success .display-4`, `.card.bg-info .display-4`, and `.card.bg-warning .display-4` are all visible and contain a number |

## Assertions
- `page.Url` contains `/Dashboard` (case-insensitive) after login.
- Breadcrumb text equals `Dashboard`.
- Each of the four `.display-4` summary card values is visible and renders digits (not an error state or blank).

## Related Code
- Location: `AuditTrailReview.UIPlaywrightTest/SmokeTests.cs`
- Test method: `ValidLoginReachesDashboardWithSummaryCards`
- Config: `TestConfig.BaseUrl`, `LoginEmail`, `LoginPassword`
- Shares the login flow already implemented in `MenuLinkCrawlerTests.LogInAsync()`

## Page Elements Used
| Element | Selector |
|---------|----------|
| Email input | `#Input_Email` |
| Password input | `#Input_Password` |
| Submit button | `#login-submit` |
| Breadcrumb | `ol.breadcrumb li.breadcrumb-item.active` |
| Total scans card | `.card.bg-primary .display-4` |
| Scans to review card | `.card.bg-success .display-4` |
| Systems card | `.card.bg-info .display-4` |
| Unreviewed findings card | `.card.bg-warning .display-4` |

## Status
Active
