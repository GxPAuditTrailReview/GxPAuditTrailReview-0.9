using AuditTrailReview.Engine.Security;
using AuditTrailReview.Model;
using Microsoft.Extensions.Configuration;

namespace AuditTrailReview.Engine.Services
{
    /// <summary>
    /// Resolves the SMTP host/port/SSL/From/credentials actually in effect right now -- the
    /// dbo.EmailSettings row (see EmailSettings.cs) when one exists and has a value for a given
    /// field, falling back field-by-field to appsettings.json's SmtpSettings section otherwise.
    /// Shared by SmtpEmailService (to send mail) and AdminPanel/Settings/Index (to display the
    /// effective values), so the two can never disagree about what's actually configured.
    ///
    /// UserName falls back the same way as Host/SenderEmail/etc. Password is decrypted from
    /// saved.PasswordEncrypted via ApiKeyProtector (same master key as AiSettings.ApiKeyEncrypted)
    /// when present, falling back to SmtpSettings:Password otherwise -- so EffectiveSmtpSettings
    /// always carries the plaintext password actually in effect, never the ciphertext.
    /// </summary>
    public static class EmailSettingsResolver
    {
        public static EffectiveSmtpSettings Resolve(EmailSettings? saved, IConfiguration configuration)
        {
            var configSection = configuration.GetSection("SmtpSettings");

            var host = !string.IsNullOrWhiteSpace(saved?.Host) ? saved!.Host : configSection["Host"];
            var port = saved?.Port ?? (int.TryParse(configSection["Port"], out var p) ? p : 587);
            var enableSsl = saved?.EnableSsl ?? (bool.TryParse(configSection["EnableSsl"], out var ssl) ? ssl : true);
            var senderEmail = !string.IsNullOrWhiteSpace(saved?.SenderEmail) ? saved!.SenderEmail : configSection["SenderEmail"];
            var senderName = !string.IsNullOrWhiteSpace(saved?.SenderName) ? saved!.SenderName! : (configSection["SenderName"] ?? "GxPAuditTrailReview");
            var userName = !string.IsNullOrWhiteSpace(saved?.UserName) ? saved!.UserName : configSection["UserName"];
            var password = !string.IsNullOrWhiteSpace(saved?.PasswordEncrypted)
                ? ApiKeyProtector.Decrypt(saved!.PasswordEncrypted, configuration)
                : configSection["Password"];

            return new EffectiveSmtpSettings(host, port, enableSsl, senderEmail, senderName, userName, password);
        }
    }

    /// <summary>The SMTP host/port/SSL/From/credentials in effect for one send -- see <see cref="EmailSettingsResolver"/>.</summary>
    public sealed record EffectiveSmtpSettings(string? Host, int Port, bool EnableSsl, string? SenderEmail, string SenderName, string? UserName, string? Password);
}
