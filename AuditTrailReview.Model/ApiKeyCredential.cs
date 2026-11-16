using System;
using System.ComponentModel.DataAnnotations;

namespace AuditTrailReview.Model
{
    /// <summary>
    /// One named API key for AuditTrailReview.IntegrationHub.API's X-Api-Key authentication
    /// (AuditTrailReview.IntegrationHub.API.Security.ApiKeyAuthenticationHandler), replacing the
    /// old static ApiKeyAuth:Keys config list. Unlike the other settings rows in this namespace
    /// (SecuritySettings/GeneralSettings/EmailSettings/AiSettings), this is NOT a singleton --
    /// there can be any number of rows, managed from AdminPanel/Settings/Index.cshtml's "API"
    /// tab, so multiple integration clients can each hold their own key and be revoked
    /// independently (rotation without downtime for the others).
    ///
    /// <see cref="EncryptedKey"/> holds the raw key encrypted at rest via
    /// AuditTrailReview.Engine.Security.ApiKeyProtector (same AES-256-GCM scheme and
    /// Security:EncryptionKey master key already used for AiSettings.ApiKeyEncrypted and
    /// EmailSettings.PasswordEncrypted) -- the database alone is never enough to recover a key.
    /// The raw key itself is shown in the AdminPanel UI exactly once, immediately after
    /// AuditTrailReview.Engine.Security.ApiKeyCredentialValidator.GenerateRawKey creates it; it
    /// is never decrypted for display again, so <see cref="KeyHint"/> (plaintext, non-secret) is
    /// kept alongside the ciphertext purely so the admin can tell rows apart in the UI.
    ///
    /// A key is revoked, never deleted: <see cref="RevokedUtc"/> null means active,
    /// non-null means revoked. The row is kept so its audit history remains meaningful.
    /// </summary>
    public class ApiKeyCredential
    {
        [Key]
        public int Id { get; set; }

        /// <summary>Admin-chosen label (e.g. the integration/caller this key was issued to).
        /// No uniqueness is enforced -- rows are told apart by <see cref="CreatedUtc"/> too.</summary>
        [Required(ErrorMessage = "Name is required.")]
        [StringLength(200, ErrorMessage = "Name must be {1} characters or fewer.")]
        public string Name { get; set; } = string.Empty;

        /// <summary>The raw key, encrypted via AuditTrailReview.Engine.Security.ApiKeyProtector.Encrypt.
        /// Never decrypted for display and deliberately excluded from every audit-trail snapshot
        /// (see IndexModel's ApiKeyCredentialAuditSnapshot), the same convention as
        /// AiSettings.ApiKeyEncrypted/EmailSettings.PasswordEncrypted.</summary>
        [Required]
        public string EncryptedKey { get; set; } = string.Empty;

        /// <summary>Last few characters of the raw key, stored in plaintext purely for display
        /// (e.g. "...7f3a") -- not sensitive on its own, far too short to help recover or guess
        /// the full key, but enough for an admin to recognize which physical key a row is.</summary>
        [Required]
        [StringLength(8)]
        public string KeyHint { get; set; } = string.Empty;

        public DateTime CreatedUtc { get; set; }

        /// <summary>Null while the key is active. Set once, to the revocation time, when an admin
        /// revokes it -- the row is kept (never hard-deleted) so its audit history stays
        /// meaningful; AuditTrailReview.Engine.Security.ApiKeyCredentialValidator only ever
        /// accepts keys from rows where this is null.</summary>
        public DateTime? RevokedUtc { get; set; }
    }
}
