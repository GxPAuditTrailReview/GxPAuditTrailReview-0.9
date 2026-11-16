# TC-803 – Application Services: Email Notification Building and Sending

## Objective
Verify `EmailTemplateBuilder.BuildActionEmail` (the shared branded HTML wrapper used by action emails such as ForgotPasswordModel's password-reset message) and `IdentityEmailSender`, the adapter that routes ASP.NET Core Identity's own `IEmailSender` calls (ForgotPassword, Register, ResendEmailConfirmation, ExternalLogin, Manage/Email) through the application's real `IEmailService` instead of Identity's built-in no-op sender.

## Preconditions
- No real SMTP connection involved. `EmailTemplateBuilder` is tested as a pure string-building function. `IdentityEmailSenderTests` uses a hand-written `SpyEmailService` (implementing `IEmailService`) in place of the real SMTP-backed service; only the forwarding behavior is tested; `SmtpEmailService` itself needs a live SMTP server and has no automated coverage (see `Test/TestStrategy.md`).

## Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call `BuildActionEmail` with a sender name, greeting name, intro text, button text, action URL, expiry note, and footer note | The HTML includes the sender name in the header, a personalized "Hello {name}," greeting, the action URL as the button's `href`, and the button text, expiry note, and footer note verbatim |
| 2 | Call it with a `null` or blank greeting name | Falls back to a generic "Hello," greeting (never renders "Hello null") |
| 3 | Call it with an action URL containing query-string special characters (`&`) | The `&` is HTML-encoded (`&amp;`) in the rendered `href` |
| 4 | Call it with a sender name containing `&` | The sender name is HTML-encoded in the header |
| 5 | Call `IdentityEmailSender.SendEmailAsync` with a to-address, subject, and HTML body | The call is forwarded to `IEmailService.SendEmailAsync` exactly once, with all three arguments passed through unchanged |
| 6 | Call `SendEmailAsync` twice with different arguments | Each call is forwarded independently; the spy records the latest call's arguments and a call count of 2 |

## Assertions
- `BuildActionEmail` never injects an unescaped literal "null" into the greeting, and always HTML-encodes caller-supplied text that lands in the output.
- `IdentityEmailSender` is a pure pass-through to `IEmailService.SendEmailAsync`; it never calls `SendCredentialsEmailAsync`.

## Related Code
- Test classes: `AuditTrailReview.UnitTest.Engine.EmailTemplateBuilderTests`, `AuditTrailReview.UnitTest.WebApp.Services.IdentityEmailSenderTests`
- Methods: all `BuildActionEmail_*` methods; `SendEmailAsync_ForwardsAllThreeArgumentsToEmailService`, `SendEmailAsync_CalledTwice_ForwardsEachCallIndependently`
- Production code: `AuditTrailReview.Engine.Services.EmailTemplateBuilder.BuildActionEmail`, `AuditTrailReview.WebApp.Services.IdentityEmailSender`

## Status
Active
