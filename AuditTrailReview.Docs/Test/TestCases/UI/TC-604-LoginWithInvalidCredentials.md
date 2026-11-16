# TC-004 – Login With Invalid Credentials

## Objective
Verify that a user cannot log in with incorrect credentials and that a meaningful error message is displayed.

## Preconditions
- Web application is running at `Config.BaseUrl`
- Browser opens in visible or headless mode as set in `Config.RunHeadless`

## Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to `/Identity/Account/Login` | Login form is displayed |
| 2 | Enter `invalid@example.com` in the Email field | Email field is populated |
| 3 | Enter `WrongPassword123!` in the Password field | Password field is populated |
| 4 | Click the **Log in** button | Form is submitted |
| 5 | Check current URL | URL still contains `/Identity/Account/Login` |
| 6 | Read the validation summary | Element with `role="alert"` is visible and contains `Invalid login attempt` |

## Assertions
- `driver.Url` contains `/Identity/Account/Login`
- Validation summary `div[role='alert'].text-danger` is displayed
- Validation summary text contains `Invalid login attempt`

## Related Code
- Test class: `AuditTrailReview.UISeleniumTest.TestCase.LoginWithInvalidCredentialsTest`
- Method: `UserCannotLoginWithInvalidCredentials`
- Page object: `AuditTrailReview.UISeleniumTest.Pages.LoginPage`

## Status
Active
