# TC-808 – Application Services: DB-Backed API Key Generation and Authentication

## Objective
Verify `AuditTrailReview.Engine.Security.ApiKeyCredentialValidator` (raw key generation, hint computation, and decrypt/compare validation against a caller-supplied list of encrypted candidate values) and `AuditTrailReview.IntegrationHub.API.Security.ApiKeyAuthenticationHandler` (the `X-Api-Key` authentication handler that calls it), which together replaced `IntegrationHub.API`'s old static `ApiKeyAuth:Keys` config list. Keys are generated/revoked from the WebApp's AdminPanel -> Settings -> API tab (TC-704) and stored encrypted via `ApiKeyProtector` (TC-806).

**2026-11-05: `ApiKeyCredentialValidator.ValidateAsync` was generalized to take `IEnumerable<string>` encrypted values instead of an `ApplicationDbContext`.** It no longer knows about `dbo.ApiKeyCredential`, `dbo.McpApiKeyCredential`, or EF Core at all -- each caller fetches its own active (`RevokedUtc == null`) rows and passes their `EncryptedKey` values in. This is what let the exact same validator be reused, unchanged, by `AuditTrailReview.McpServer.Security.McpApiKeyMiddleware` for its own, separate `dbo.McpApiKeyCredential` pool (see TC-807) without either service being able to accept the other's keys.

## Preconditions
- `ApiKeyCredentialValidatorTests` needs no `ApplicationDbContext` or EF Core at all any more -- it builds plain `string[]`/`List<string>` encrypted-value lists directly, plus an `IConfiguration` via `ConfigurationBuilder().AddInMemoryCollection(...)` (`Security:EncryptionKey`), same pattern as `ApiKeyProtectorTests` (TC-806).
- `ApiKeyAuthenticationHandlerTests` constructs `ApiKeyAuthenticationHandler` directly (no running host) with a fixed `IOptionsMonitor<ApiKeyAuthenticationOptions>`, `NullLoggerFactory`, `UrlEncoder.Default`, an EF Core InMemory `ApplicationDbContext` seeded with `ApiKeyCredential` rows, and the same kind of `IConfiguration`; `InitializeAsync`/`AuthenticateAsync` are called directly on the handler.

## Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | `ApiKeyCredentialValidator.GenerateRawKey()` called twice | Two different values, each free of `+`/`/`/`=` (header-safe) and prefixed `atr_` |
| 2 | `ApiKeyCredentialValidator.ComputeKeyHint(rawKey)` | Returns the raw key's last 4 characters |
| 3 | `ValidateAsync` with an empty encrypted-keys list | Returns `false` |
| 4 | `ValidateAsync` with one encrypted value that decrypts to the presented key | Returns `true` |
| 5 | `ValidateAsync` with the presented key not matching the one encrypted value given | Returns `false` |
| 6 | `ValidateAsync` with an empty list standing in for "the only key is revoked" (the validator has no revocation concept itself -- a revoked key's value simply isn't passed in by the caller) | Returns `false` |
| 7 | `ValidateAsync` with several encrypted values, presenting the key for one of them | Returns `true` |
| 8 | `ValidateAsync` with `Security:EncryptionKey` missing/invalid | Returns `false` without throwing |
| 9 | `ValidateAsync` with one undecryptable value (not valid ciphertext) alongside one genuinely matching value | Returns `true` -- one bad value doesn't block the others |
| 10 | `ValidateAsync` with a null or empty presented key | Returns `false` |
| 11 | `ApiKeyAuthenticationHandler.AuthenticateAsync` with no `X-Api-Key` header | Fails |
| 12 | `ApiKeyAuthenticationHandler.AuthenticateAsync` with an empty header value | Fails |
| 13 | `ApiKeyAuthenticationHandler.AuthenticateAsync` with no active `ApiKeyCredential` rows in the database | Fails |
| 14 | `ApiKeyAuthenticationHandler.AuthenticateAsync` with a valid active key | Succeeds; the resulting principal's identity name is `"ApiKeyClient"` and carries a `permission=scan:write` claim |
| 15 | `ApiKeyAuthenticationHandler.AuthenticateAsync` with a revoked key | Fails |
| 16 | `ApiKeyAuthenticationHandler.AuthenticateAsync` with the wrong key | Fails |

## Assertions
- A generated raw key is always safe to send verbatim in an HTTP header and is never persisted in plaintext anywhere -- only its `ApiKeyProtector`-encrypted form (`EncryptedKey`) and its short, non-secret `KeyHint` are stored.
- `ApiKeyCredentialValidator` itself has no notion of "revoked" or any particular table -- filtering to active rows is entirely the caller's responsibility (`ApiKeyAuthenticationHandler` and `McpApiKeyMiddleware` each do their own `Where(k => k.RevokedUtc == null)` against their own table before calling it).
- A missing/invalid master key (`Security:EncryptionKey`) degrades validation to a consistent `false` (401 at the handler/middleware level) rather than throwing or returning a 500; one unreadable encrypted value among several does not block validation of the rest.
- The handler's claims (`ClaimTypes.Name = "ApiKeyClient"`, `permission = scan:write`) are unchanged from the old config-based scheme -- only where the key itself is looked up changed.
- `ApiKeyAuthenticationOptions.HeaderName` remains configuration-bound (`ApiKeyAuth:HeaderName`) and is still used to read the presented key from the request.

## Related Code
- Test classes: `AuditTrailReview.UnitTest.Engine.Security.ApiKeyCredentialValidatorTests`, `AuditTrailReview.UnitTest.ApiKeyAuthenticationHandlerTests`
- Methods: `GenerateRawKey_ReturnsHeaderSafeUniqueValues`, `ComputeKeyHint_ReturnsLastFourChars`, `ValidateAsync_NoActiveKeys_ReturnsFalse`, `ValidateAsync_OneMatchingEncryptedKey_ReturnsTrue`, `ValidateAsync_WrongKey_ReturnsFalse`, `ValidateAsync_RevokedKeyNotIncludedByCaller_ReturnsFalse`, `ValidateAsync_MultipleEncryptedKeysOneMatches_ReturnsTrue`, `ValidateAsync_MissingMasterKey_ReturnsFalseWithoutThrowing`, `ValidateAsync_OneUndecryptableValueDoesNotBlockOthers_ReturnsTrue`, `ValidateAsync_NullOrEmptyPresentedKey_ReturnsFalse`, `AuthenticateAsync_MissingHeader_Fails`, `AuthenticateAsync_EmptyHeader_Fails`, `AuthenticateAsync_NoActiveKeysInDatabase_Fails`, `AuthenticateAsync_ValidActiveKey_SucceedsWithExpectedClaims`, `AuthenticateAsync_RevokedKey_Fails`, `AuthenticateAsync_WrongKey_Fails`
- Production code: `AuditTrailReview.Engine.Security.ApiKeyCredentialValidator`, `AuditTrailReview.IntegrationHub.API.Security.ApiKeyAuthenticationHandler`/`ApiKeyAuthenticationOptions`, `AuditTrailReview.Model.ApiKeyCredential`, `AuditTrailReview.Engine.Security.ApiKeyProtector` (TC-806)
- Related: TC-807 (`McpApiKeyMiddleware`, the other caller of this same validator, against its own separate `McpApiKeyCredential` table)

## Notes
- The rate-limiting middleware in `AuditTrailReview.IntegrationHub.API/Program.cs` that partitions/logs by presented API key (reading only `ApiKeyAuthenticationOptions.HeaderName`, unaffected by this change) is not re-tested here; see its own existing coverage, if any, for that behavior.
- There is no automated end-to-end test of a real HTTP request through `IntegrationHub.API`'s middleware pipeline with a key generated by the WebApp's API tab against a real (non-InMemory) database -- that cutover should be verified manually once per environment (generate a key, call the API with it, revoke it, confirm the next call is rejected).
- The previously shipped placeholder key (`"change-me-api-key"`) in `appsettings.json` is intentionally not migrated into `dbo.ApiKeyCredential` -- any caller relying on it will get `401` after this change until an admin generates and distributes a real key.

## Status
Active

## Change history
- 2026-11-05: `ApiKeyCredentialValidator.ValidateAsync` generalized from `(ApplicationDbContext, ...)` to `(IEnumerable<string> encryptedKeys, ...)` so it could be reused unchanged by `McpApiKeyMiddleware`/`McpApiKeyCredential` (TC-807) as well as `ApiKeyAuthenticationHandler`/`ApiKeyCredential`. Test method names updated to match (e.g. `ValidateAsync_OneActiveMatchingKey_ReturnsTrue` -> `ValidateAsync_OneMatchingEncryptedKey_ReturnsTrue`); added `ValidateAsync_OneUndecryptableValueDoesNotBlockOthers_ReturnsTrue` to cover the "skip one bad value, keep checking the rest" behavior directly (previously only reachable indirectly through the master-key-missing case).
