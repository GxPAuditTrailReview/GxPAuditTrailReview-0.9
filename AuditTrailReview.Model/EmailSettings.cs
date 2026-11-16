using System.ComponentModel.DataAnnotations;

namespace AuditTrailReview.Model
{
    /// <summary>
    /// Singleton configuration row (always Id = 1) for the SMTP settings -- host, port, SSL,
    /// From address/name, and (since the UserName/PasswordEncrypted columns were added) the SMTP
    /// account itself. Mirrors SecuritySettings/EngineSettings' singleton-row convention.
    ///
    /// <see cref="PasswordEncrypted"/> holds the SMTP password encrypted (see
    /// AuditTrailReview.Engine.Security.ApiKeyProtector, the same envelope-encryption helper
    /// AiSettings.ApiKeyEncrypted uses) with the same separate master key that stays in
    /// configuration (Security:EncryptionKey) -- the database alone is never enough to recover
    /// it, and the Email tab's password field never redisplays it once saved.
    /// <see cref="UserName"/> is plain text (not secret on its own -- typically an email address
    /// or account name) and, like every other field here, falls back field-by-field to
    /// appsettings.json's SmtpSettings section when this row has no value for it -- see
    /// EmailSettingsResolver. Everything here is safe to store and show except
    /// PasswordEncrypted's ciphertext, and can be changed by an admin
    /// (AdminPanel/Settings/Index, Email tab) without a code change or redeploy.
    /// Unlike SecuritySettings/Identity, a save here takes effect on the very next email sent --
    /// SmtpEmailService re-reads this row on every send rather than through a startup-bound
    /// options object.
    /// </summary>
    public class EmailSettings
    {
        [Key]
        public int Id { get; set; }

        [Display(Name = "SMTP host")]
        [Required(ErrorMessage = "SMTP host is required.")]
        public string? Host { get; set; }

        [Display(Name = "SMTP port")]
        [Range(1, 65535, ErrorMessage = "Port must be between {1} and {2}.")]
        public int Port { get; set; } = 587;

        [Display(Name = "Use SSL/TLS")]
        public bool EnableSsl { get; set; } = true;

        [Display(Name = "From address")]
        [Required(ErrorMessage = "From address is required.")]
        [EmailAddress(ErrorMessage = "Enter a valid email address.")]
        public string? SenderEmail { get; set; }

        [Display(Name = "From name")]
        public string? SenderName { get; set; } = "GxPAuditTrailReview";

        /// <summary>The SMTP account's username (often the same as the From address, but not
        /// necessarily). Null when not configured here -- EmailSettingsResolver then falls back
        /// to appsettings.json's SmtpSettings:UserName.</summary>
        [Display(Name = "SMTP username")]
        public string? UserName { get; set; }

        /// <summary>The SMTP account's password, encrypted at rest via
        /// AuditTrailReview.Engine.Security.ApiKeyProtector.Encrypt. Null when no password has
        /// been saved here yet (EmailSettingsResolver then falls back to appsettings.json's
        /// SmtpSettings:Password). Never decrypted for display -- the Email tab's password field
        /// is always blank on load -- and deliberately excluded from every audit-trail
        /// before/after snapshot built in IndexModel.OnPostSaveEmailAsync, so the ciphertext is
        /// never written into dbo.AuditTrail either (same convention as
        /// AiSettings.ApiKeyEncrypted).</summary>
        public string? PasswordEncrypted { get; set; }
    }
}
