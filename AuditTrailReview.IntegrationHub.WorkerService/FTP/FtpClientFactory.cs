using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AuditTrailReview.Engine.Services;
using FluentFTP;
using FluentEncryptionMode = FluentFTP.FtpEncryptionMode;
using ModelEncryptionMode = AuditTrailReview.Model.FtpEncryptionMode;

namespace AuditTrailReview.IntegrationHub.WorkerService.FTP;

/// <summary>
/// Builds the FluentFTP client for one poll from the resolved settings: plain FTP, explicit FTPS
/// (AUTH TLS) or implicit FTPS, with the server certificate checked by
/// <see cref="FtpSettingsResolver.IsCertificateAcceptable"/>. Never uses ValidateAnyCertificate --
/// accepting every certificate happens only through the explicit "Validate server certificate = off"
/// setting.
/// </summary>
public static class FtpClientFactory
{
    public static AsyncFtpClient Create(EffectiveFtpSettings settings)
    {
        var client = new AsyncFtpClient(settings.Host, settings.Username, settings.Password, settings.Port);

        if (settings.EncryptionMode == ModelEncryptionMode.None)
        {
            client.Config.EncryptionMode = FluentEncryptionMode.None;
            return client;
        }

        client.Config.EncryptionMode = settings.EncryptionMode == ModelEncryptionMode.Implicit
            ? FluentEncryptionMode.Implicit
            : FluentEncryptionMode.Explicit;
        client.Config.DataConnectionEncryption = true;

        client.ValidateCertificate += (_, e) =>
        {
            var certificate = e.Certificate;
            string? sha1 = null, sha256 = null;
            if (certificate is not null)
            {
                var raw = certificate.GetRawCertData();
                sha1 = Convert.ToHexString(SHA1.HashData(raw));
                sha256 = Convert.ToHexString(SHA256.HashData(raw));
            }

            e.Accept = FtpSettingsResolver.IsCertificateAcceptable(
                settings.ValidateCertificate,
                settings.CertificateThumbprint,
                e.PolicyErrors == System.Net.Security.SslPolicyErrors.None,
                sha1,
                sha256);
        };

        return client;
    }
}
