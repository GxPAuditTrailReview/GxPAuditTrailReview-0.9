# TC-UI-003 – Invalid Credentials Are Rejected With a Clear Error

## Objective
Verify that a login attempt with incorrect credentials fails, stays on the login page, and shows a clear validation message, rather than silently failing or crashing.

## Preconditions
- `AuditTrailReview.WebApp` is running and reachable at `TestConfig.BaseUrl`.
- No valid account needed — this uses deliberately wrong credentials.

## Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to `/Identity/Account/Login` | Login form is displayed |
| 2 | Fill `#Input_Email` with `invalid@example.com` | Field is populated |
| 3 | Fill `#Input_Password` with `WrongPassword123!` | Field is populated |
| 4 | Click `#login-submit` | Form submits |
| 5 | Check current URL | URL still contains `/Identity/Account/Login` |
| 6 | Read the validation summary | Element matching `div[role='alert'].text-danger` is visible and contains `Invalid login attempt` |

## Assertions
- `page.Url` contains `/Identity/Account/Login` after submitting.
- `div[role='alert'].text-danger` is visible and its text contains `Invalid login attempt`.

## Related Code
- Location: `AuditTrailReview.UIPlaywrightTest/SmokeTests.cs`
- Test method: `InvalidCredentialsAreRejectedWithError`
- Config: `TestConfig.BaseUrl`
- Playwright counterpart to the existing Selenium case `TC-604-LoginWithInvalidCredentials`; written independently since it targets a different test project

## Page Elements Used
| Element | Selector |
|---------|----------|
| Email input | `#Input_Email` |
| Password input | `#Input_Password` |
| Submit button | `#login-submit` |
| Validation summary | `div[role='alert'].text-danger` |

## Status
Active
