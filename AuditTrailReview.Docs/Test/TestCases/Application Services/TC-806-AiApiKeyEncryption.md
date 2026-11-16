# TC-806 – Application Services: AI API Key Encryption

## Objective
Verify `AuditTrailReview.Engine.Security.ApiKeyProtector`, the AES-256-GCM helper that lets the OpenAI API key be stored (encrypted) in `dbo.AiSettings.ApiKeyEncrypted` instead of a plaintext config file, so an admin can change the key from `AdminPanel/Settings`' AI Analysis tab without editing config or redeploying. The master key used to encrypt/decrypt it stays in configuration (`Security:EncryptionKey`, named `Security:AiApiKeyEncryptionKey` before the Email tab's SMTP password started sharing the same helper/key), never in the database -- a database breach alone does not expose the OpenAI API key.

## Preconditions
- No web server or database involved: `ApiKeyProtector`'s static `Encrypt`/`Decrypt` methods are called directly, each with its own `IConfiguration` built via `ConfigurationBuilder().AddInMemoryCollection(...)`.
- A valid master key for these tests is a base64 string that decodes to exactly 32 bytes (e.g. `Convert.ToBase64String(new byte[32])`).

## Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | `Encrypt` a plaintext value, then `Decrypt` the result with the same master key | The original plaintext is returned, and the ciphertext itself never equals the plaintext |
| 2 | Call `Encrypt` twice with the same plaintext and the same master key | The two ciphertexts differ (a fresh random nonce is used every call) |
| 3 | `Decrypt(null, ...)` / `Decrypt("", ...)` | Returns `null` -- "no key configured yet" is not an error |
| 4 | `Encrypt`/`Decrypt` with `Security:EncryptionKey` missing from configuration | Both throw `InvalidOperationException` with a message naming the missing config key |
| 5 | `Encrypt` with a master key that decodes to a length other than 32 bytes, or that isn't valid base64 | Throws `InvalidOperationException` |
| 6 | `Decrypt` ciphertext produced with one master key, using a different (but validly-shaped) master key | Throws a `CryptographicException` (authentication tag mismatch) -- a wrong key can never silently decrypt to the wrong plaintext |

## Assertions
- `Encrypt`/`Decrypt` round-trip correctly under the same master key, and never reveal the plaintext in the ciphertext string.
- Every `Encrypt` call uses a fresh nonce, so identical plaintexts never produce identical ciphertext.
- A missing, malformed, or wrong-length master key fails loudly (`InvalidOperationException`) rather than silently proceeding or falling back.
- A ciphertext can only be decrypted with the exact master key it was encrypted with; any other key fails authentication rather than returning corrupted plaintext.

## Related Code
- Test class: `AuditTrailReview.UnitTest.Engine.Security.ApiKeyProtectorTests`
- Methods: `EncryptThenDecrypt_SameMasterKey_ReturnsOriginalPlaintext`, `Encrypt_CalledTwiceWithSamePlaintext_ProducesDifferentCiphertext`, `Decrypt_NullCiphertext_ReturnsNull`, `Decrypt_EmptyCiphertext_ReturnsNull`, `Encrypt_MasterKeyMissing_ThrowsInvalidOperationException`, `Decrypt_MasterKeyMissing_ThrowsInvalidOperationException`, `Encrypt_MasterKeyWrongLength_ThrowsInvalidOperationException`, `Encrypt_MasterKeyNotBase64_ThrowsInvalidOperationException`, `Decrypt_DifferentMasterKey_ThrowsCryptographicException`
- Production code: `AuditTrailReview.Engine.Security.ApiKeyProtector`
- Related: TC-703 (`AdminPanel/Settings`' AI Analysis tab save handler, which calls `Encrypt` when saving a new key and whose `OpenAIService` call path calls `Decrypt` when resolving it)

## Notes
- Does not cover `OpenAIService`'s own use of `ApiKeyProtector.Decrypt` when resolving credentials (falling back to `configuration["OpenAI:ApiKey"]` when no DB key is set) -- that is exercised indirectly by `OpenAIService`'s existing "never actually called" test doubles (`DocChatServiceTests.cs`), which this project's convention deliberately keeps from making real OpenAI network calls (see `IdentityEmailSenderTests.cs`'s remarks and `Traceability Matrix.md` Section 6).

## Status
Active
