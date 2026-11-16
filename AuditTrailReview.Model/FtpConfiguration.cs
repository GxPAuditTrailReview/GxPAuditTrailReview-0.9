using System.ComponentModel.DataAnnotations;

namespace AuditTrailReview.Model
{
    /// <summary>How the Worker Service's FTP client secures the connection to the customer's server.</summary>
    public enum FtpEncryptionMode
    {
        /// <summary>Plain FTP. Credentials and audit-trail files travel in clear text.</summary>
        [Display(Name = "None (plain FTP, not encrypted)")]
        None = 0,

        /// <summary>FTPS with AUTH TLS: connects on the normal FTP port, then upgrades to TLS.</summary>
        [Display(Name = "Explicit FTPS (AUTH TLS, port 21)")]
        Explicit = 1,

        /// <summary>FTPS where TLS starts immediately (conventionally port 990).</summary>
        [Display(Name = "Implicit FTPS (port 990)")]
        Implicit = 2
    }

    /// <summary>
    /// Singleton configuration row (always Id = 1) for the Worker Service's FTP ingestion, edited in
    /// AdminPanel/Settings/Index (FTP tab). Replaces the old appsettings.json "Ftp" section, which is
    /// now only used once, to seed this row on first start (see WebApp Program.cs SeedAsync).
    ///
    /// <see cref="Enabled"/> is the master switch and is OFF by default: while off, the Worker does
    /// not connect to any FTP server (it still processes queued ScanUploadJob rows -- API uploads
    /// depend on that). The Worker re-reads this row on every poll, so a save takes effect without a
    /// restart. <see cref="PasswordEncrypted"/> is encrypted with ApiKeyProtector (same master key
    /// as EmailSettings.PasswordEncrypted) and is never redisplayed or written to the audit trail.
    ///
    /// No [Required] attributes on purpose: a fresh, disabled row is legitimately empty, and
    /// "required when enabled" is checked in IndexModel.OnPostSaveFtpAsync.
    /// </summary>
    public class FtpConfiguration
    {
        [Key]
        public int Id { get; set; }

        [Display(Name = "Enable FTP ingestion")]
        public bool Enabled { get; set; }

        [Display(Name = "FTP host")]
        public string? Host { get; set; }

        /// <summary>Null means the protocol default: 21 for None/Explicit, 990 for Implicit.</summary>
        [Display(Name = "Port")]
        [Range(1, 65535, ErrorMessage = "Port must be between {1} and {2}.")]
        public int? Port { get; set; }

        [Display(Name = "Transport security")]
        public FtpEncryptionMode EncryptionMode { get; set; } = FtpEncryptionMode.Explicit;

        /// <summary>FTPS only. When true the server certificate must chain to a trusted root, or
        /// match <see cref="CertificateThumbprint"/>. Turning this off accepts any certificate
        /// (encrypts, but does not authenticate the server) and is discouraged.</summary>
        [Display(Name = "Validate server certificate")]
        public bool ValidateCertificate { get; set; } = true;

        /// <summary>FTPS only. Optional SHA-1/SHA-256 thumbprint (hex) of a self-signed or
        /// internal-CA server certificate to trust explicitly.</summary>
        [Display(Name = "Pinned certificate thumbprint")]
        public string? CertificateThumbprint { get; set; }

        [Display(Name = "FTP username")]
        public string? Username { get; set; }

        public string? PasswordEncrypted { get; set; }

        [Display(Name = "Remote folder")]
        public string RemoteFolder { get; set; } = "/";

        [Display(Name = "Processed subfolder")]
        public string? ProcessedFolder { get; set; } = "Processed";

        [Display(Name = "Failed subfolder")]
        public string? FailedFolder { get; set; } = "Failed";

        [Display(Name = "Polling interval (seconds)")]
        [Range(5, 86400, ErrorMessage = "Polling interval must be between {1} and {2} seconds.")]
        public int PollingIntervalSeconds { get; set; } = 30;

        [Display(Name = "Stale unmoved file threshold (minutes)")]
        [Range(1, 100000, ErrorMessage = "Stale threshold must be at least {1} minute.")]
        public int StaleUnmovedFileThresholdMinutes { get; set; } = 60;

        [Display(Name = "Stale file check interval (minutes)")]
        [Range(1, 100000, ErrorMessage = "Stale check interval must be at least {1} minute.")]
        public int StaleFileCheckIntervalMinutes { get; set; } = 60;

        /// <summary>The registered system this Worker instance ingests files for (one Worker
        /// instance ingests for exactly one system -- ADR-0005/0008).</summary>
        [Display(Name = "System")]
        public int? SystemId { get; set; }
    }
}
