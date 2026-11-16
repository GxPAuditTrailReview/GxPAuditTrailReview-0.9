using AuditTrailReview.Engine.Security;
using AuditTrailReview.Model;
using Microsoft.Extensions.Configuration;

namespace AuditTrailReview.Engine.Services
{
    /// <summary>
    /// Turns the dbo.FtpConfiguration row (see FtpConfiguration.cs) into the runtime settings the
    /// Worker Service's FTP client uses: password decrypted via ApiKeyProtector, default port filled
    /// in, a missing row treated as "disabled". Unlike EmailSettingsResolver there is no appsettings
    /// fallback -- the database row is the single source of truth (the old appsettings "Ftp" section
    /// is only used to seed the row once).
    /// </summary>
    public static class FtpSettingsResolver
    {
        public const int DefaultFtpPort = 21;
        public const int DefaultImplicitFtpsPort = 990;

        public static EffectiveFtpSettings Resolve(FtpConfiguration? saved, IConfiguration configuration)
        {
            if (saved is null)
                return EffectiveFtpSettings.Disabled;

            var password = string.IsNullOrWhiteSpace(saved.PasswordEncrypted)
                ? string.Empty
                : ApiKeyProtector.Decrypt(saved.PasswordEncrypted, configuration) ?? string.Empty;

            return new EffectiveFtpSettings(
                Enabled: saved.Enabled,
                Host: saved.Host?.Trim() ?? string.Empty,
                Port: EffectivePort(saved.Port, saved.EncryptionMode),
                EncryptionMode: saved.EncryptionMode,
                ValidateCertificate: saved.ValidateCertificate,
                CertificateThumbprint: NormalizeThumbprint(saved.CertificateThumbprint),
                Username: saved.Username ?? string.Empty,
                Password: password,
                RemoteFolder: string.IsNullOrWhiteSpace(saved.RemoteFolder) ? "/" : saved.RemoteFolder,
                ProcessedFolder: saved.ProcessedFolder ?? string.Empty,
                FailedFolder: saved.FailedFolder ?? string.Empty,
                PollingIntervalSeconds: Math.Max(saved.PollingIntervalSeconds, 5),
                StaleUnmovedFileThresholdMinutes: Math.Max(saved.StaleUnmovedFileThresholdMinutes, 1),
                StaleFileCheckIntervalMinutes: Math.Max(saved.StaleFileCheckIntervalMinutes, 1),
                SystemId: saved.SystemId ?? 0);
        }

        /// <summary>The configured port, or the protocol default (990 for implicit FTPS, else 21).</summary>
        public static int EffectivePort(int? configuredPort, FtpEncryptionMode mode) =>
            configuredPort is > 0
                ? configuredPort.Value
                : mode == FtpEncryptionMode.Implicit ? DefaultImplicitFtpsPort : DefaultFtpPort;

        /// <summary>Removes the separators people paste thumbprints with (spaces, colons, dashes)
        /// and upper-cases it; null when nothing is left.</summary>
        public static string? NormalizeThumbprint(string? thumbprint)
        {
            if (string.IsNullOrWhiteSpace(thumbprint))
                return null;

            var cleaned = new string(thumbprint.Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();
            return cleaned.Length == 0 ? null : cleaned;
        }

        /// <summary>
        /// Decides whether to accept an FTPS server certificate. Plain FTP never reaches this.
        /// A pinned thumbprint always wins (exact match required, whatever the chain says);
        /// otherwise, when validation is on, only a clean chain is accepted. Validation off accepts
        /// anything (encrypted but unauthenticated).
        /// </summary>
        public static bool IsCertificateAcceptable(
            bool validateCertificate,
            string? pinnedThumbprint,
            bool chainIsValid,
            string? certificateSha1Hex,
            string? certificateSha256Hex)
        {
            var pinned = NormalizeThumbprint(pinnedThumbprint);
            if (pinned is not null)
                return string.Equals(pinned, NormalizeThumbprint(certificateSha1Hex), StringComparison.Ordinal)
                    || string.Equals(pinned, NormalizeThumbprint(certificateSha256Hex), StringComparison.Ordinal);

            return !validateCertificate || chainIsValid;
        }
    }

    /// <summary>The FTP settings in effect for one poll -- see <see cref="FtpSettingsResolver"/>.</summary>
    public sealed record EffectiveFtpSettings(
        bool Enabled,
        string Host,
        int Port,
        FtpEncryptionMode EncryptionMode,
        bool ValidateCertificate,
        string? CertificateThumbprint,
        string Username,
        string Password,
        string RemoteFolder,
        string ProcessedFolder,
        string FailedFolder,
        int PollingIntervalSeconds,
        int StaleUnmovedFileThresholdMinutes,
        int StaleFileCheckIntervalMinutes,
        int SystemId)
    {
        public static EffectiveFtpSettings Disabled { get; } = new(
            false, string.Empty, FtpSettingsResolver.DefaultFtpPort, FtpEncryptionMode.Explicit, true, null,
            string.Empty, string.Empty, "/", "Processed", "Failed", 30, 60, 60, 0);
    }
}
