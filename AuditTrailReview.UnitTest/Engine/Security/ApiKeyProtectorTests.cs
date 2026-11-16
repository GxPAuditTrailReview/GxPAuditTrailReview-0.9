using System;
using System.Collections.Generic;
using AuditTrailReview.Engine.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AuditTrailReview.UnitTest.Engine.Security
{
    [TestClass]
    public class ApiKeyProtectorTests
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
        public void EncryptThenDecrypt_SameMasterKey_ReturnsOriginalPlaintext()
        {
            var configuration = CreateConfiguration(GenerateValidMasterKeyBase64());

            var ciphertext = ApiKeyProtector.Encrypt("sk-test-12345", configuration);
            var plaintext = ApiKeyProtector.Decrypt(ciphertext, configuration);

            Assert.AreEqual("sk-test-12345", plaintext);
            Assert.AreNotEqual("sk-test-12345", ciphertext);
        }

        [TestMethod]
        public void Encrypt_CalledTwiceWithSamePlaintext_ProducesDifferentCiphertext()
        {
            var configuration = CreateConfiguration(GenerateValidMasterKeyBase64());

            var first = ApiKeyProtector.Encrypt("sk-test-12345", configuration);
            var second = ApiKeyProtector.Encrypt("sk-test-12345", configuration);

            Assert.AreNotEqual(first, second);
        }

        [TestMethod]
        public void Decrypt_NullCiphertext_ReturnsNull()
        {
            var configuration = CreateConfiguration(GenerateValidMasterKeyBase64());

            Assert.IsNull(ApiKeyProtector.Decrypt(null, configuration));
        }

        [TestMethod]
        public void Decrypt_EmptyCiphertext_ReturnsNull()
        {
            var configuration = CreateConfiguration(GenerateValidMasterKeyBase64());

            Assert.IsNull(ApiKeyProtector.Decrypt(string.Empty, configuration));
        }

        [TestMethod]
        public void Encrypt_MasterKeyMissing_ThrowsInvalidOperationException()
        {
            var configuration = CreateConfiguration(masterKeyBase64: null);

            try
            {
                ApiKeyProtector.Encrypt("sk-test", configuration);
                Assert.Fail("Expected InvalidOperationException was not thrown.");
            }
            catch (InvalidOperationException)
            {
                // expected
            }
        }

        [TestMethod]
        public void Decrypt_MasterKeyMissing_ThrowsInvalidOperationException()
        {
            var withKey = CreateConfiguration(GenerateValidMasterKeyBase64());
            var ciphertext = ApiKeyProtector.Encrypt("sk-test", withKey);

            var withoutKey = CreateConfiguration(masterKeyBase64: null);

            try
            {
                ApiKeyProtector.Decrypt(ciphertext, withoutKey);
                Assert.Fail("Expected InvalidOperationException was not thrown.");
            }
            catch (InvalidOperationException)
            {
                // expected
            }
        }

        [TestMethod]
        public void Encrypt_MasterKeyWrongLength_ThrowsInvalidOperationException()
        {
            var configuration = CreateConfiguration(Convert.ToBase64String(new byte[16]));

            try
            {
                ApiKeyProtector.Encrypt("sk-test", configuration);
                Assert.Fail("Expected InvalidOperationException was not thrown.");
            }
            catch (InvalidOperationException)
            {
                // expected
            }
        }

        [TestMethod]
        public void Encrypt_MasterKeyNotBase64_ThrowsInvalidOperationException()
        {
            var configuration = CreateConfiguration("not-valid-base64!!!");

            try
            {
                ApiKeyProtector.Encrypt("sk-test", configuration);
                Assert.Fail("Expected InvalidOperationException was not thrown.");
            }
            catch (InvalidOperationException)
            {
                // expected
            }
        }

        [TestMethod]
        public void Decrypt_DifferentMasterKey_ThrowsCryptographicException()
        {
            var keyA = CreateConfiguration(GenerateValidMasterKeyBase64());
            var ciphertext = ApiKeyProtector.Encrypt("sk-test", keyA);

            var differentKeyBytes = new byte[32];
            differentKeyBytes[0] = 1;
            var keyB = CreateConfiguration(Convert.ToBase64String(differentKeyBytes));

            try
            {
                ApiKeyProtector.Decrypt(ciphertext, keyB);
                Assert.Fail("Expected a decryption/authentication failure but none was thrown.");
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                // expected -- AesGcm throws on authentication tag mismatch (the concrete
                // exception type has varied across .NET versions, so catch the common base).
            }
        }
    }
}
