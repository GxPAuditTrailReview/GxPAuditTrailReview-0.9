using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace AuditTrailReview.Engine.Security
{
    /// <summary>
    /// Encrypts/decrypts secrets (AiSettings.ApiKeyEncrypted and, since EmailSettingsResolver
    /// gained SMTP credential resolution, EmailSettings.PasswordEncrypted too) for storage in a
    /// database column, using AES-256-GCM with a master key that itself stays in configuration
    /// (Security:EncryptionKey) rather than in the database. This "envelope" split is the whole
    /// point: a database breach alone no longer exposes the OpenAI API key or SMTP password, and
    /// an admin can rotate either from its own settings tab without editing config files or
    /// redeploying -- only the rarely-changed master key itself needs config-file access,
    /// deployed identically to every process that calls Decrypt (WebApp and
    /// IntegrationHub.WorkerService; see OpenAIService's call sites). Deliberately named/keyed
    /// generically (not "AiApiKeyEncryptionKey", its name before EmailSettings.PasswordEncrypted
    /// started using it too) and shared by both secrets rather than adding a second master key --
    /// one secret per process to rotate and deploy, not two.
    /// </summary>
    public static class ApiKeyProtector
    {
        private const string ConfigKey = "Security:EncryptionKey";
        private const int NonceSizeBytes = 12;
        private const int TagSizeBytes = 16;

        public static string Encrypt(string plaintext, IConfiguration configuration)
        {
            if (plaintext == null) throw new ArgumentNullException(nameof(plaintext));

            var key = ResolveMasterKey(configuration);
            var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);

            var nonce = new byte[NonceSizeBytes];
            RandomNumberGenerator.Fill(nonce);
            var tag = new byte[TagSizeBytes];
            var ciphertext = new byte[plaintextBytes.Length];

            using var aesGcm = new AesGcm(key, TagSizeBytes);
            aesGcm.Encrypt(nonce, plaintextBytes, ciphertext, tag);

            var combined = new byte[NonceSizeBytes + TagSizeBytes + ciphertext.Length];
            Buffer.BlockCopy(nonce, 0, combined, 0, NonceSizeBytes);
            Buffer.BlockCopy(tag, 0, combined, NonceSizeBytes, TagSizeBytes);
            Buffer.BlockCopy(ciphertext, 0, combined, NonceSizeBytes + TagSizeBytes, ciphertext.Length);

            return Convert.ToBase64String(combined);
        }

        public static string? Decrypt(string? ciphertext, IConfiguration configuration)
        {
            if (string.IsNullOrEmpty(ciphertext)) return null;

            var key = ResolveMasterKey(configuration);
            var combined = Convert.FromBase64String(ciphertext);

            if (combined.Length < NonceSizeBytes + TagSizeBytes)
                throw new InvalidOperationException("Stored AI API key value is too short to be valid ciphertext.");

            var nonce = new byte[NonceSizeBytes];
            var tag = new byte[TagSizeBytes];
            var encrypted = new byte[combined.Length - NonceSizeBytes - TagSizeBytes];
            Buffer.BlockCopy(combined, 0, nonce, 0, NonceSizeBytes);
            Buffer.BlockCopy(combined, NonceSizeBytes, tag, 0, TagSizeBytes);
            Buffer.BlockCopy(combined, NonceSizeBytes + TagSizeBytes, encrypted, 0, encrypted.Length);

            var plaintextBytes = new byte[encrypted.Length];
            using var aesGcm = new AesGcm(key, TagSizeBytes);
            aesGcm.Decrypt(nonce, encrypted, tag, plaintextBytes);

            return Encoding.UTF8.GetString(plaintextBytes);
        }

        private static byte[] ResolveMasterKey(IConfiguration configuration)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));

            var configured = configuration[ConfigKey];
            if (string.IsNullOrWhiteSpace(configured))
                throw new InvalidOperationException(
                    $"{ConfigKey} is not configured. Generate one (e.g. " +
                    "[Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32)) " +
                    "in PowerShell) and set the same value on every process that saves or reads an encrypted secret (the OpenAI API key or the SMTP password).");

            byte[] key;
            try
            {
                key = Convert.FromBase64String(configured);
            }
            catch (FormatException ex)
            {
                throw new InvalidOperationException($"{ConfigKey} is not valid base64.", ex);
            }

            if (key.Length != 32)
                throw new InvalidOperationException($"{ConfigKey} must decode to exactly 32 bytes (AES-256); got {key.Length}.");

            return key;
        }
    }
}
