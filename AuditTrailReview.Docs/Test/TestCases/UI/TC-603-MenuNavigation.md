# TC-003 – User Can Navigate All Menu Items

## Objective
Verify that a logged-in user can open every item in the navigation menu and that each click loads the correct page.

## Preconditions
- Web application is running at `Config.BaseUrl`
- `Config.LoginEmail` is set to a valid user account
- `Config.LoginPassword` is set to the correct password
- Browser opens in visible mode (`Config.RunHeadless = false`) so the navigation is observable
- For **Config panel** steps: user must have the role `Admin` or `Super user`
- For **Admin panel** steps: user must have the role `Admin`

## Test Steps

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Navigate to `/Identity/Account/Login` | Login form is displayed |
| 2 | Enter credentials and click **Log in** | User is redirected to Dashboard |
| 3 | Click **Dashboard** in the navbar | URL contains `/Dashboard` |
| 4 | Wait 2 seconds | Dashboard page is visible |
| 5 | Click **Systems** in the navbar | URL contains `/System` |
| 6 | Wait 2 seconds | Systems page is visible |
| 7 | Open **Scans** dropdown, click **All scans** | URL contains `/Scan` |
| 8 | Wait 2 seconds | All scans page is visible |
| 9 | *(Admin / Super user only)* Open **Config panel** dropdown, click **Rule-based review engine** | Page loads |
| 10 | *(Admin / Super user only)* Wait 2 seconds, navigate back | Previous page is restored |
| 11 | *(Admin / Super user only)* Open **Config panel** dropdown, click **System type** | URL contains `/SystemType` |
| 12 | *(Admin / Super user only)* Wait 2 seconds | System type page is visible |
| 13 | *(Admin only)* Open **Admin panel** dropdown, click **Users** | URL contains `/AdminPanel/User` |
| 14 | *(Admin only)* Wait 2 seconds | Users page is visible |
| 15 | *(Admin only)* Open **Admin panel** dropdown, click **Roles** | URL contains `/AdminPanel/Role` |
| 16 | *(Admin only)* Wait 2 seconds | Roles page is visible |
| 17 | Open **Logs** dropdown, click **User login log** | URL contains `/Log/UserLogin` |
| 18 | Wait 2 seconds | User login log page is visible |
| 19 | Open **Logs** dropdown, click **Audit trail log** | URL contains `/Log/AuditTrail` |
| 20 | Wait 2 seconds | Audit trail log page is visible |

## Assertions

| After step | Assert |
|------------|--------|
| 3 | `driver.Url` contains `/Dashboard` |
| 5 | `driver.Url` contains `/System` |
| 7 | `driver.Url` contains `/Scan` |
| 11 | `driver.Url` contains `/SystemType` |
| 13 | `driver.Url` contains `/AdminPanel/User` |
| 15 | `driver.Url` contains `/AdminPanel/Role` |
| 17 | `driver.Url` contains `/Log/UserLogin` |
| 19 | `driver.Url` contains `/Log/AuditTrail` |

## Page Elements Used

| Element | Selector | File |
|---------|----------|------|
| Dashboard link | `a.nav-link[href*='/Dashboard']` | `Pages/NavMenuPage.cs` |
| Systems link | `a.nav-link[href*='/System']` | `Pages/NavMenuPage.cs` |
| Scans dropdown toggle | text = `Scans` | `Pages/NavMenuPage.cs` |
| All scans item | `a.dropdown-item[href*='/Scan']` | `Pages/NavMenuPage.cs` |
| Config panel dropdown toggle | text = `Config panel` | `Pages/NavMenuPage.cs` |
| Rule-based review engine item | `a.dropdown-item[href*='RuleBasedReviewEngine']` | `Pages/NavMenuPage.cs` |
| System type item | `a.dropdown-item[id='menuCustomer']` | `Pages/NavMenuPage.cs` |
| Admin panel dropdown toggle | text = `Admin panel` | `Pages/NavMenuPage.cs` |
| Users item | `a.dropdown-item[href*='/AdminPanel/User']` | `Pages/NavMenuPage.cs` |
| Roles item | `a.dropdown-item[href*='/AdminPanel/Role']` | `Pages/NavMenuPage.cs` |
| Logs dropdown toggle | text = `Logs` | `Pages/NavMenuPage.cs` |
| User login log item | `a.dropdown-item[href*='/Log/UserLogin']` | `Pages/NavMenuPage.cs` |
| Audit trail log item | `a.dropdown-item[href*='/Log/AuditTrail']` | `Pages/NavMenuPage.cs` |

## Related Code

- Test class: `AuditTrailReview.UISeleniumTest.TestCase.MenuNavigationTest`
- Method: `UserCanNavigateAllMenuItems`
- Page objects:
  - `Pages/LoginPage.cs`
  - `Pages/DashboardPage.cs`
  - `Pages/NavMenuPage.cs`

## Notes
- Dropdown clicks use JavaScript (`JsClick`) to avoid `ElementClickInterceptedException`
- Any previously open dropdown is closed automatically before the next one is opened
- Config panel and Admin panel steps are skipped automatically if the logged-in user does not have the required role

## Status
Active
