using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AuditTrailReview.Engine.Security
{
    /// <summary>
    /// Generates and validates the named, DB-stored API keys that replaced two previously-static
    /// config key lists: IntegrationHub.API's old ApiKeyAuth:Keys (now AuditTrailReview.Model.
    /// ApiKeyCredential) and the MCP server's old McpAuth:Keys (now AuditTrailReview.Model.
    /// McpApiKeyCredential). Deliberately knows nothing about either table or DbContext --
    /// callers fetch their own active rows' encrypted values and pass them in -- so the same
    /// generate/decrypt/compare logic is shared by both WebApp (AdminPanel/Settings/Index.cshtml.cs
    /// generates keys for both tabs) and the two services that validate them
    /// (ApiKeyAuthenticationHandler, McpApiKeyMiddleware), the same "shared Engine helper, used by
    /// multiple processes" role ApiKeyProtector itself already has.
    /// </summary>
    public static class ApiKeyCredentialValidator
    {
        private const string KeyPrefix = "atr_";
        private const int RawKeyBytes = 32;

        /// <summary>Generates a new raw (plaintext) API key: 32 random bytes, Base64Url-encoded
        /// (no '+', '/', '=' -- safe to send verbatim in an HTTP header) and prefixed so a leaked
        /// key is recognizable/greppable in logs. The caller is responsible for showing this value
        /// to the admin exactly once and storing only its encrypted form (via ApiKeyProtector.Encrypt)
        /// -- nothing here persists the raw value.</summary>
        public static string GenerateRawKey()
        {
            var bytes = RandomNumberGenerator.GetBytes(RawKeyBytes);
            var encoded = Convert.ToBase64String(bytes)
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('=');

            return KeyPrefix + encoded;
        }

        /// <summary>The last 4 characters of a raw key, for a credential row's KeyHint display
        /// only -- far too short to help recover or guess the full key.</summary>
        public static string ComputeKeyHint(string rawKey)
        {
            if (string.IsNullOrEmpty(rawKey))
                throw new ArgumentException("Raw key must not be null or empty.", nameof(rawKey));

            return rawKey.Length <= 4 ? rawKey : rawKey[^4..];
        }

        /// <summary>
        /// Checks <paramref name="presentedKey"/> (as received in a request header) against every
        /// value in <paramref name="encryptedKeys"/> -- the caller's own active (RevokedUtc ==
        /// null) credential rows' encrypted values, from whichever table it owns -- decrypting
        /// each and comparing in constant time. Returns false -- never throws -- when the
        /// presented key is null/empty, <paramref name="encryptedKeys"/> is empty, or the master
        /// key (Security:EncryptionKey) is missing/invalid, so a configuration problem degrades to
        /// a 401 on every request rather than a 500. A single value that fails to decrypt (e.g. it
        /// was encrypted under a master key that has since been rotated out) is logged and skipped
        /// without blocking validation of the others; a missing/invalid master key fails
        /// identically for every value, so only the first such failure is logged, not once per
        /// value. Never reveals which value (if any) matched.
        /// </summary>
        public static async Task<bool> ValidateAsync(
            IEnumerable<string> encryptedKeys,
            IConfiguration configuration,
            string? presentedKey,
            ILogger logger)
        {
            var index = await FindMatchIndexAsync(encryptedKeys, configuration, presentedKey, logger);
            return index >= 0;
        }

        /// <summary>
        /// Same check as <see cref="ValidateAsync"/>, but returns the zero-based position within
        /// <paramref name="encryptedKeys"/> of the value that matched, or -1 when none did. Lets a
        /// caller whose credential rows carry an identity (e.g. McpApiKeyCredential.UserId) learn
        /// WHICH key authenticated the request, without this class knowing anything about the
        /// table: the caller enumerates its rows once into a list and indexes back into it.
        /// </summary>
        public static Task<int> FindMatchIndexAsync(
            IEnumerable<string> encryptedKeys,
            IConfiguration configuration,
            string? presentedKey,
            ILogger logger)
        {
            if (string.IsNullOrEmpty(presentedKey))
                return Task.FromResult(-1);

            var masterKeyFailureLogged = false;
            var index = -1;

            foreach (var encrypted in encryptedKeys)
            {
                index++;
                string? decrypted;
                try
                {
                    decrypted = ApiKeyProtector.Decrypt(encrypted, configuration);
                }
                catch (InvalidOperationException ex)
                {
                    // Security:EncryptionKey itself is missing/invalid -- fails identically for
                    // every value, so log once and stop trying the rest.
                    if (!masterKeyFailureLogged)
                    {
                        logger.LogError(ex, "Cannot validate API keys: Security:EncryptionKey is not configured correctly.");
                        masterKeyFailureLogged = true;
                    }

                    return Task.FromResult(-1);
                }
                catch (Exception ex)
                {
                    // Only this one value is unreadable (e.g. encrypted under a since-rotated-out
                    // master key) -- skip it and keep checking the others.
                    logger.LogWarning(ex, "Could not decrypt a stored API key value while validating a presented key.");
                    continue;
                }

                if (decrypted != null && ConstantTimeEquals(decrypted, presentedKey))
                    return Task.FromResult(index);
            }

            return Task.FromResult(-1);
        }

        /// <summary>Constant-time string comparison, moved out of
        /// AuditTrailReview.IntegrationHub.API.Security.ApiKeyAuthenticationHandler (where it was
        /// previously private) so every caller shares one implementation.</summary>
        internal static bool ConstantTimeEquals(string left, string right)
        {
            var leftBytes = Encoding.UTF8.GetBytes(left);
            var rightBytes = Encoding.UTF8.GetBytes(right);

            return leftBytes.Length == rightBytes.Length
                && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
        }
    }
}
