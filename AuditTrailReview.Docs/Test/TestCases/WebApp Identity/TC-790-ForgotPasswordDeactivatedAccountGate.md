# TC-790 – WebApp Identity: Forgot Password Deactivated-Account Gate

## Objective
Verify `ForgotPasswordModel.OnPostAsync` (`Areas/Identity/Pages/Account/ForgotPassword.cshtml.cs`) refuses to send a password-reset email for a deactivated account, treating it the same as an unknown or unconfirmed one -- the same generic confirmation page is shown either way, so the response never reveals whether an account exists, is unconfirmed, or is deactivated.

## Preconditions
- `ForgotPasswordModel` is constructed with a real `UserManager<ApplicationUser>` (via `PageModelTestHelpers`, backed by an EF Core InMemory `ApplicationDbContext`), a spy `IEmailSender` that records how many times `SendEmailAsync` was called, and an empty `IConfiguration` (the `SmtpSettings:SenderName` fallback is exercised, not the actual value).
- Only the deactivated-account gate is covered here, since it's the only branch of `OnPostAsync` that returns before calling `Url.Page(...)` -- the confirmed/active-account success path needs the same `IUrlHelper` plumbing `PageModelTestHelpers`' remarks note isn't wired up yet (see `AdminPanel/User/Index.cshtml.cs`'s `OnPostSendCredentialsAsync`), so it stays out of scope for this pass.

## Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | `OnPostAsync` with the email of a deactivated (confirmed) account | No email is sent; redirects to `./ForgotPasswordConfirmation` |
| 2 | `OnPostAsync` with an email that doesn't match any account | No email is sent; redirects to the same `./ForgotPasswordConfirmation` page |

## Assertions
- A deactivated account never receives a working password-reset link, even though resetting the password wouldn't have restored sign-in access anyway (Login.cshtml.cs's lockout check runs before the password is verified) -- this avoids handing out a pointless reset link.
- The deactivated case and the unknown-email case redirect to the exact same page, so the response carries no signal distinguishing "doesn't exist" from "exists but is deactivated or unconfirmed".

## Related Code
- Test classes: `AuditTrailReview.UnitTest.WebApp.Areas.Identity.Pages.Account.ForgotPasswordModelTests`
- Methods: `OnPostAsync_DeactivatedAccount_DoesNotSendEmailAndRedirectsToConfirmation`, `OnPostAsync_UnknownEmail_DoesNotSendEmailAndRedirectsToConfirmation`
- Production code: `AuditTrailReview.WebApp.Areas.Identity.Pages.Account.ForgotPasswordModel`, `AuditTrailReview.WebApp.Helpers.UserAccountStatus`
- Related: TC-805 (the `UserAccountStatus.IsDeactivated` check this gate reuses); TC-702 (where an account is deactivated from)

## Notes
The confirmed/active-account success path (email actually sent, reset link built via `Url.Page`) is not covered by an automated test for the reason given in Preconditions; it remains manual, same as `AdminPanel/User/Index.cshtml.cs`'s `OnPostSendCredentialsAsync`.

## Status
Active
