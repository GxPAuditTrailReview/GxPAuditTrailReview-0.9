# TC-002 – User Can Log In and See Dashboard

## Objective
Verify that a valid user can log in through the login form and is redirected to the Dashboard page, with the correct breadcrumb visible.

## Preconditions
- Web application is running at `Config.BaseUrl`
- `Config.LoginEmail` is set to a valid user account
- `Config.LoginPassword` is set to the correct password for that account
- Browser opens in visible or headless mode as set in `Config.RunHeadless`

## Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to `/Identity/Account/Login` | Login form is displayed |
| 2 | Enter `Config.LoginEmail` into the Email field | Field is filled |
| 3 | Enter `Config.LoginPassword` into the Password field | Field is filled |
| 4 | Click the `Log in` button | Form is submitted |
| 5 | Wait for the Dashboard page to load | Dashboard breadcrumb becomes visible |
| 6 | Read current URL | URL contains `/Dashboard` |
| 7 | Read breadcrumb text | Breadcrumb text equals `Dashboard` |
| 8 | Wait 5 seconds | Dashboard remains visible |

## Assertions
- `driver.Url` contains `/Dashboard` (case-insensitive)
- Dashboard breadcrumb text equals `Dashboard`

## Related Code
- Test class: `AuditTrailReview.UISeleniumTest.TestCase.DashboardTest`
- Method: `UserCanLogInAndSeeDashboard`
- Page objects:
  - `AuditTrailReview.UISeleniumTest.Pages.LoginPage`
  - `AuditTrailReview.UISeleniumTest.Pages.DashboardPage`

## Page Elements Used
| Element | Selector | File |
|---------|----------|------|
| Email input | `#Input_Email` | `Pages/LoginPage.cs` |
| Password input | `#Input_Password` | `Pages/LoginPage.cs` |
| Submit button | `#login-submit` | `Pages/LoginPage.cs` |
| Breadcrumb | `ol.breadcrumb li.breadcrumb-item.active` | `Pages/DashboardPage.cs` |
| Total scans card | `.card.bg-primary .display-4` | `Pages/DashboardPage.cs` |
| Scans to review card | `.card.bg-success .display-4` | `Pages/DashboardPage.cs` |
| Systems card | `.card.bg-info .display-4` | `Pages/DashboardPage.cs` |
| Unreviewed findings card | `.card.bg-warning .display-4` | `Pages/DashboardPage.cs` |

## Status
Active
