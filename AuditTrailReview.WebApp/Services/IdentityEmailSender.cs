using AuditTrailReview.Engine.Services;
using Microsoft.AspNetCore.Identity.UI.Services;

namespace AuditTrailReview.WebApp.Services
{
    /// <summary>
    /// Adapts our SMTP-backed <see cref="IEmailService"/> (AuditTrailReview.Engine) onto ASP.NET
    /// Core Identity's own <see cref="IEmailSender"/> interface, which is what every scaffolded
    /// Identity page actually depends on: ForgotPassword, Register, RegisterConfirmation,
    /// ResendEmailConfirmation, ExternalLogin, and Areas/Identity/Pages/Account/Manage/Email.
    ///
    /// Without this adapter, <c>AddDefaultIdentity&lt;ApplicationUser&gt;</c>'s own internal
    /// <c>AddDefaultUI()</c> call registers the framework's built-in no-op
    /// <c>Microsoft.AspNetCore.Identity.UI.Services.EmailSender</c> as <see cref="IEmailSender"/>
    /// -- an internal stub whose <c>SendEmailAsync</c> just returns <c>Task.CompletedTask</c>
    /// without sending anything, by design (it exists purely so DI resolves without error out of
    /// the box; Microsoft's own docs call out that it must be replaced with a real sender). That
    /// made every one of the pages above silently "succeed" (they still redirect to their own
    /// confirmation page, since revealing whether an email address exists/was emailed is a
    /// deliberate security choice, not a bug) without ever actually sending an email -- reported
    /// by the user as "/Identity/Account/ForgotPassword doesn't work" even though the working SMTP
    /// credentials were sitting right there in appsettings.Development.json the whole time, just
    /// wired to a different consumer: <see cref="IEmailService"/>/<see cref="SmtpEmailService"/>
    /// only backs AdminPanel/User's own "send credentials" button
    /// (<c>IndexModel.OnPostSendCredentialsAsync</c>), a separate, custom interface that was never
    /// connected to Identity's own email hooks.
    ///
    /// Registered in Program.cs as <c>AddTransient&lt;IEmailSender, IdentityEmailSender&gt;()</c>
    /// *after* <c>AddDefaultIdentity</c> -- ASP.NET Core's DI container resolves a single-instance
    /// constructor dependency to the *last* registration for that service type, so this
    /// deliberately shadows the framework's own no-op registration rather than needing to remove
    /// it.
    /// </summary>
    public class IdentityEmailSender : IEmailSender
    {
        private readonly IEmailService _emailService;

        public IdentityEmailSender(IEmailService emailService)
        {
            _emailService = emailService;
        }

        public Task SendEmailAsync(string email, string subject, string htmlMessage)
            => _emailService.SendEmailAsync(email, subject, htmlMessage);
    }
}
