using System.Net;
using System.Net.Mail;
using System.Text.Encodings.Web;
using AuditTrailReview.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace AuditTrailReview.Engine.Services
{
    public class SmtpEmailService : IEmailService
    {
        private readonly IConfiguration _configuration;
        private readonly ApplicationDbContext _context;

        public SmtpEmailService(IConfiguration configuration, ApplicationDbContext context)
        {
            _configuration = configuration;
            _context = context;
        }

        public async Task SendCredentialsEmailAsync(string toEmail, string userName, string passwordResetUrl, string? displayName = null)
        {
            var effective = await ResolveEffectiveSettingsAsync();

            var greeting = string.IsNullOrWhiteSpace(displayName)
                ? "Hello,"
                : $"Hello {HtmlEncoder.Default.Encode(displayName)},";

            var body = $"{greeting}<br /><br />" +
                       $"An account has been created for you in <strong>{HtmlEncoder.Default.Encode(effective.SenderName)}</strong>.<br /><br />" +
                       $"Your login: <strong>{HtmlEncoder.Default.Encode(userName)}</strong><br /><br />" +
                       $"To get started, please set your password by <a href='{HtmlEncoder.Default.Encode(passwordResetUrl)}'>clicking here</a>. " +
                       $"This link is valid for 24 hours and can only be used once.<br /><br />" +
                       $"If you weren't expecting this email, please contact your system administrator.<br /><br />" +
                       $"Regards,<br />" +
                       $"{HtmlEncoder.Default.Encode(effective.SenderName)}";

            await SendSmtpEmailAsync(toEmail, $"Your {effective.SenderName} login credentials", body, effective);
        }

        public async Task SendEmailAsync(string toEmail, string subject, string htmlBody)
        {
            var effective = await ResolveEffectiveSettingsAsync();
            await SendSmtpEmailAsync(toEmail, subject, htmlBody, effective);
        }

        /// <summary>
        /// dbo.EmailSettings (AdminPanel/Settings/EditEmail) when a row exists, falling back
        /// field-by-field to appsettings.json's SmtpSettings section otherwise -- see
        /// EmailSettingsResolver. Re-read on every send (no caching), so a saved change takes
        /// effect immediately, unlike SecuritySettings/IdentityOptions which is bound once at
        /// startup.
        /// </summary>
        private async Task<EffectiveSmtpSettings> ResolveEffectiveSettingsAsync()
        {
            var saved = await _context.EmailSettings.AsNoTracking().FirstOrDefaultAsync();
            return EmailSettingsResolver.Resolve(saved, _configuration);
        }

        /// <summary>
        /// Shared SMTP send used by every <see cref="IEmailService"/> method -- the one place a
        /// real SmtpClient connection is made, so SendCredentialsEmailAsync and SendEmailAsync
        /// can never drift on host/port/sender/credentials. UserName/Password now come from
        /// <paramref name="effective"/> (dbo.EmailSettings when saved there, falling back to
        /// SmtpSettings:UserName/Password otherwise -- see EmailSettingsResolver), the same
        /// resolution every other field here already went through.
        /// </summary>
        private async Task SendSmtpEmailAsync(string toEmail, string subject, string htmlBody, EffectiveSmtpSettings effective)
        {
            var host = effective.Host ?? throw new InvalidOperationException("The SMTP host is not configured (SmtpSettings:Host or AdminPanel/Settings/Index's Email tab).");
            var senderEmail = effective.SenderEmail ?? throw new InvalidOperationException("The SMTP sender address is not configured (SmtpSettings:SenderEmail or AdminPanel/Settings/Index's Email tab).");
            var userName = effective.UserName ?? throw new InvalidOperationException("The SMTP username is not configured (SmtpSettings:UserName or AdminPanel/Settings/Index's Email tab).");
            var password = effective.Password ?? throw new InvalidOperationException("The SMTP password is not configured (SmtpSettings:Password or AdminPanel/Settings/Index's Email tab).");

            using var message = new MailMessage
            {
                From = new MailAddress(senderEmail, effective.SenderName),
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true
            };
            message.To.Add(new MailAddress(toEmail));

            using var client = new SmtpClient(host, effective.Port)
            {
                EnableSsl = effective.EnableSsl,
                Credentials = new NetworkCredential(userName, password)
            };

            await client.SendMailAsync(message);
        }
    }
}
