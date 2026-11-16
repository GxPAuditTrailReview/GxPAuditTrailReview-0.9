# TC-001 – User Can See Home Page

## Objective
Verify that an unauthenticated user can open the application home page and see the correct brand and page title.

## Preconditions
- Web application is running at `Config.BaseUrl`
- Browser opens in visible or headless mode as set in `Config.RunHeadless`

## Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to `Config.BaseUrl` | Browser loads without error |
| 2 | Wait 5 seconds | Page is fully rendered |
| 3 | Read navbar brand text | Text equals `GxPAuditTrailReview` |
| 4 | Read browser tab title | Title contains `GxPAuditTrailReview` |
| 5 | Check current URL | URL starts with `Config.BaseUrl` |

## Assertions
- `driver.Url` starts with `Config.BaseUrl`
- Navbar brand text equals `GxPAuditTrailReview`
- Page title contains `GxPAuditTrailReview`

## Related Code
- Test class: `AuditTrailReview.UISeleniumTest.TestCase.HomePageVisibleTest`
- Method: `UserCanSeeHomePage`
- Page selector file: none (selectors inline in test)

## Status
Active
