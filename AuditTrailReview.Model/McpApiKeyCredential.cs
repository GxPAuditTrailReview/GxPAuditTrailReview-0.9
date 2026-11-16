using System;
using System.ComponentModel.DataAnnotations;

namespace AuditTrailReview.Model
{
    /// <summary>
    /// One named API key for AuditTrailReview.McpServer's <c>/mcp</c> endpoint
    /// (AuditTrailReview.McpServer.Security.McpApiKeyMiddleware), replacing the old static
    /// McpAuth:Keys config list. Deliberately a separate table from
    /// <see cref="ApiKeyCredential"/> (IntegrationHub.API's own keys), managed from its own "MCP"
    /// tab on AdminPanel/Settings/Index.cshtml -- the two key pools are disjoint on purpose, so a
    /// key issued for one service can never authenticate the other (the MCP server only grants
    /// read-only QA data access; IntegrationHub.API grants scan:write).
    ///
    /// Otherwise identical in shape and conventions to <see cref="ApiKeyCredential"/>: not a
    /// singleton (any number of rows), <see cref="EncryptedKey"/> holds the raw key encrypted at
    /// rest via AuditTrailReview.Engine.Security.ApiKeyProtector (same master key,
    /// Security:EncryptionKey), the raw key is shown in the AdminPanel UI exactly once via
    /// AuditTrailReview.Engine.Security.ApiKeyCredentialValidator.GenerateRawKey and never
    /// decrypted for display again, and a key is revoked (via <see cref="RevokedUtc"/>) rather
    /// than deleted so its audit history stays meaningful.
    /// </summary>
    public class McpApiKeyCredential
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
        /// (see IndexModel's McpApiKeyCredentialAuditSnapshot).</summary>
        [Required]
        public string EncryptedKey { get; set; } = string.Empty;

        /// <summary>Last few characters of the raw key, stored in plaintext purely for display
        /// (e.g. "...7f3a") -- not sensitive on its own, far too short to help recover or guess
        /// the full key, but enough for an admin to recognize which physical key a row is.</summary>
        [Required]
        [StringLength(8)]
        public string KeyHint { get; set; } = string.Empty;

        /// <summary>Optional ASP.NET Identity user id (AspNetUsers.Id, same convention as
        /// ScanInfo.UserId) this key was issued to. When set, McpApiKeyMiddleware attributes every
        /// request made with the key to that person (log scope + the request's ClaimsPrincipal);
        /// when null the key is a shared/service key and requests are attributed to
        /// <see cref="Name"/> only. Not a foreign key: a deleted user must not block or cascade to
        /// the key row, whose history has to stay meaningful.</summary>
        [StringLength(450)]
        public string? UserId { get; set; }

        public DateTime CreatedUtc { get; set; }

        /// <summary>Null while the key is active. Set once, to the revocation time, when an admin
        /// revokes it -- the row is kept (never hard-deleted) so its audit history stays
        /// meaningful; AuditTrailReview.Engine.Security.ApiKeyCredentialValidator only ever
        /// accepts keys from rows where this is null.</summary>
        public DateTime? RevokedUtc { get; set; }
    }
}
