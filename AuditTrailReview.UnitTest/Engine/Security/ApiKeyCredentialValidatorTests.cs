using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AuditTrailReview.Engine.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AuditTrailReview.UnitTest.Engine.Security
{
    /// <summary>
    /// ApiKeyCredentialValidator is deliberately table-agnostic -- it only ever sees a plain list
    /// of already-fetched encrypted values, never an ApplicationDbContext -- so these tests build
    /// that list directly rather than seeding an InMemory database. It is shared by
    /// AuditTrailReview.Model.ApiKeyCredential (IntegrationHub.API) and
    /// AuditTrailReview.Model.McpApiKeyCredential (the MCP server); nothing here is specific to
    /// either table.
    /// </summary>
    [TestClass]
    public class ApiKeyCredentialValidatorTests
    {
        private const string ConfigKey = "Security:EncryptionKey";

        private static IConfiguration CreateConfiguration(string? masterKeyBase64)
        {
            var values = new Dictionary<string, string?>();
            if (masterKeyBase64 != null) values[ConfigKey] = masterKeyBase64;
            return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        }

        private static string GenerateValidMasterKeyBase64() => Convert.ToBase64String(new byte[32]);

        [TestMethod]
        public void GenerateRawKey_ReturnsHeaderSafeUniqueValues()
        {
            var first = ApiKeyCredentialValidator.GenerateRawKey();
            var second = ApiKeyCredentialValidator.GenerateRawKey();

            Assert.AreNotEqual(first, second);
            Assert.IsFalse(first.Contains('+') || first.Contains('/') || first.Contains('='));
            Assert.IsTrue(first.StartsWith("atr_"));
        }

        [TestMethod]
        public void ComputeKeyHint_ReturnsLastFourChars()
        {
            Assert.AreEqual("7f3a", ApiKeyCredentialValidator.ComputeKeyHint("atr_abcdef1234567f3a"));
        }

        [TestMethod]
        public async Task ValidateAsync_NoActiveKeys_ReturnsFalse()
        {
            var configuration = CreateConfiguration(GenerateValidMasterKeyBase64());

            var isValid = await ApiKeyCredentialValidator.ValidateAsync(
                Array.Empty<string>(), configuration, "some-key", NullLogger.Instance);

            Assert.IsFalse(isValid);
        }

        [TestMethod]
        public async Task ValidateAsync_OneMatchingEncryptedKey_ReturnsTrue()
        {
            var configuration = CreateConfiguration(GenerateValidMasterKeyBase64());
            var rawKey = ApiKeyCredentialValidator.GenerateRawKey();
            var encryptedKeys = new[] { ApiKeyProtector.Encrypt(rawKey, configuration) };

            var isValid = await ApiKeyCredentialValidator.ValidateAsync(encryptedKeys, configuration, rawKey, NullLogger.Instance);

            Assert.IsTrue(isValid);
        }

        [TestMethod]
        public async Task ValidateAsync_WrongKey_ReturnsFalse()
        {
            var configuration = CreateConfiguration(GenerateValidMasterKeyBase64());
            var rawKey = ApiKeyCredentialValidator.GenerateRawKey();
            var encryptedKeys = new[] { ApiKeyProtector.Encrypt(rawKey, configuration) };

            var isValid = await ApiKeyCredentialValidator.ValidateAsync(encryptedKeys, configuration, "wrong-key", NullLogger.Instance);

            Assert.IsFalse(isValid);
        }

        [TestMethod]
        public async Task ValidateAsync_RevokedKeyNotIncludedByCaller_ReturnsFalse()
        {
            // The validator itself has no concept of "revoked" -- it only ever sees what the
            // caller passes in. Callers (ApiKeyAuthenticationHandler, McpApiKeyMiddleware) are
            // responsible for filtering to RevokedUtc == null before calling ValidateAsync; this
            // test exercises that contract from the validator's side: a revoked key's encrypted
            // value simply isn't in the list, so it can never match.
            var configuration = CreateConfiguration(GenerateValidMasterKeyBase64());
            var rawKey = ApiKeyCredentialValidator.GenerateRawKey();

            var isValid = await ApiKeyCredentialValidator.ValidateAsync(
                Array.Empty<string>(), configuration, rawKey, NullLogger.Instance);

            Assert.IsFalse(isValid);
        }

        [TestMethod]
        public async Task ValidateAsync_MultipleEncryptedKeysOneMatches_ReturnsTrue()
        {
            var configuration = CreateConfiguration(GenerateValidMasterKeyBase64());
            var rawKeys = Enumerable.Range(0, 3).Select(_ => ApiKeyCredentialValidator.GenerateRawKey()).ToList();
            var encryptedKeys = rawKeys.Select(k => ApiKeyProtector.Encrypt(k, configuration)).ToList();

            var isValid = await ApiKeyCredentialValidator.ValidateAsync(encryptedKeys, configuration, rawKeys[2], NullLogger.Instance);

            Assert.IsTrue(isValid);
        }

        [TestMethod]
        public async Task ValidateAsync_MissingMasterKey_ReturnsFalseWithoutThrowing()
        {
            var validConfiguration = CreateConfiguration(GenerateValidMasterKeyBase64());
            var rawKey = ApiKeyCredentialValidator.GenerateRawKey();
            var encryptedKeys = new[] { ApiKeyProtector.Encrypt(rawKey, validConfiguration) };

            var noKeyConfiguration = CreateConfiguration(masterKeyBase64: null);

            var isValid = await ApiKeyCredentialValidator.ValidateAsync(encryptedKeys, noKeyConfiguration, rawKey, NullLogger.Instance);

            Assert.IsFalse(isValid);
        }

        [TestMethod]
        public async Task ValidateAsync_OneUndecryptableValueDoesNotBlockOthers_ReturnsTrue()
        {
            var configuration = CreateConfiguration(GenerateValidMasterKeyBase64());
            var rawKey = ApiKeyCredentialValidator.GenerateRawKey();
            var encryptedKeys = new[] { "not-valid-ciphertext", ApiKeyProtector.Encrypt(rawKey, configuration) };

            var isValid = await ApiKeyCredentialValidator.ValidateAsync(encryptedKeys, configuration, rawKey, NullLogger.Instance);

            Assert.IsTrue(isValid);
        }

        [TestMethod]
        public async Task ValidateAsync_NullOrEmptyPresentedKey_ReturnsFalse()
        {
            var configuration = CreateConfiguration(GenerateValidMasterKeyBase64());

            Assert.IsFalse(await ApiKeyCredentialValidator.ValidateAsync(Array.Empty<string>(), configuration, null, NullLogger.Instance));
            Assert.IsFalse(await ApiKeyCredentialValidator.ValidateAsync(Array.Empty<string>(), configuration, string.Empty, NullLogger.Instance));
        }
    }
}
