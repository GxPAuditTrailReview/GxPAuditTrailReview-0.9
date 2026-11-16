namespace AuditTrailReview.Engine.Services
{
    public interface IEmailService
    {
        Task SendCredentialsEmailAsync(string toEmail, string userName, string passwordResetUrl, string? displayName = null);

        /// <summary>
        /// Sends a plain ad-hoc email (subject + HTML body) over the same configured SMTP
        /// connection as <see cref="SendCredentialsEmailAsync"/>. Added so that ASP.NET Core
        /// Identity's own scaffolded pages (ForgotPassword, Register, ResendEmailConfirmation,
        /// Manage/Email, ExternalLogin) -- which depend on the framework's own
        /// Microsoft.AspNetCore.Identity.UI.Services.IEmailSender, not this interface -- can be
        /// wired to send through the same real SMTP settings via a thin adapter
        /// (AuditTrailReview.WebApp.Services.IdentityEmailSender), instead of Identity's built-in
        /// no-op IEmailSender (which silently discards every email it's asked to send).
        /// </summary>
        Task SendEmailAsync(string toEmail, string subject, string htmlBody);
    }
}
