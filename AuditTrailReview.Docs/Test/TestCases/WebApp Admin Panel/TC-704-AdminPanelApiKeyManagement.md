# TC-704 – WebApp Admin Panel: Application Settings API Key Management

## Objective
Verify the "API" tab on the consolidated Application Settings page (`Pages/AdminPanel/Settings/Index.cshtml.cs`'s `IndexModel`): `OnPostGenerateApiKeyAsync`, `OnPostRevokeApiKeyAsync`, and the key listing loaded by `LoadApiKeysAsync`. This tab manages `dbo.ApiKeyCredential`, the named, independently revocable API keys `AuditTrailReview.IntegrationHub.API`'s `X-Api-Key` authentication now validates against (see TC-808), replacing the old static `ApiKeyAuth:Keys` config list. Unlike the other tabs on this page, this is not a singleton settings row -- any number of keys may exist, each generated and revoked independently.

**2026-11-05: a sibling "MCP" tab (TC-705) was added for the MCP server's own, separate `dbo.McpApiKeyCredential` key pool.** The two tabs are structurally identical but deliberately independent -- see TC-705's own isolation test (`OnPostGenerateApiKeyAsync_DoesNotCreateMcpApiKeyCredentialRow`).

## Preconditions
- `IndexModel` is constructed with an EF Core InMemory `ApplicationDbContext`, a real `AuditTrailService` (TC-801), and (for `OnPostGenerateApiKeyAsync`, which calls `ApiKeyProtector.Encrypt`) an `IConfiguration` seeded with a valid base64 32-byte `Security:EncryptionKey` -- same `CreateConfigurationWithEncryptionKey()` helper the AI/Email tab tests use (TC-703, TC-806).
- `PageModelTestHelpers.InitializePageContext` wires up a signed-in user and `TempData`.

## Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | `OnPostGenerateApiKeyAsync` with a non-blank `NewApiKeyName` | A new `dbo.ApiKeyCredential` row is created with that `Name`, a non-empty `EncryptedKey`, and `RevokedUtc == null`; the raw (plaintext) key is stashed in `TempData["GeneratedApiKeyPlaintext"]` and decrypts back to the exact same value via `ApiKeyProtector.Decrypt`; the row's `KeyHint` is the raw key's last 4 characters |
| 2 | Same as step 1, then inspect every `dbo.AuditTrail` entry for `Object == "ApiKeyCredential"` from that call | At least one entry is logged, none with `Field == "EncryptedKey"`, and no entry's `OldValue`/`NewValue` contains the raw plaintext key |
| 3 | `OnPostGenerateApiKeyAsync` with a blank/whitespace-only `NewApiKeyName` | A `RedirectToPageResult` is returned (validation failure) and no row is added to `dbo.ApiKeyCredential` |
| 4 | `OnPostRevokeApiKeyAsync` on an existing active key's `Id` | `RevokedUtc` is set (non-null) on that row; the row count in `dbo.ApiKeyCredential` is unchanged (revoke never deletes); a `dbo.AuditTrail` entry with `Field == "RevokedUtc"` is logged |
| 5 | `OnPostRevokeApiKeyAsync` on a key that is already revoked | No-op: no additional `dbo.AuditTrail` entry is logged |
| 6 | `OnPostRevokeApiKeyAsync` with an `Id` that does not exist | No-op: `RedirectToPageResult` returned, no row created or changed |
| 7 | `OnGetAsync` with two existing `ApiKeyCredential` rows of different `CreatedUtc` | `IndexModel.ApiKeys` lists both, newest `CreatedUtc` first |

## Assertions
- The raw API key is never persisted anywhere -- only `ApiKeyProtector`-encrypted ciphertext (`EncryptedKey`) and the non-secret `KeyHint` are stored -- and is surfaced to the admin exactly once, via `TempData`, immediately after generation.
- `ApiKeyCredentialAuditSnapshot` (the private DTO `OnPostGenerateApiKeyAsync`/`OnPostRevokeApiKeyAsync` pass to `AuditTrailService`) has no `EncryptedKey` or `Id` property at all, so the ciphertext can never reach `dbo.AuditTrail` even by accident, the same structural-exclusion technique `AiSettingsAuditSnapshot` uses (TC-703, TC-806).
- Revoking a key is a soft update (`RevokedUtc` set), never a row deletion, so its audit history (including the original "generated" entry) stays intact.
- `NewApiKeyName` is required server-side before a key is generated.
- `ApiKeys` is ordered newest-first with no pagination (the number of keys is expected to stay small, unlike the Audit Trail tab's paginated entries).

## Related Code
- Test class: `AuditTrailReview.UnitTest.WebApp.AdminPanel.Settings.IndexModelTests`
- Methods: `OnPostGenerateApiKeyAsync_ValidName_CreatesEncryptedRowAndStashesPlaintextOnce`, `OnPostGenerateApiKeyAsync_BlankName_DoesNotCreateRow`, `OnPostRevokeApiKeyAsync_ActiveKey_SetsRevokedUtcAndKeepsRow`, `OnPostRevokeApiKeyAsync_AlreadyRevokedKey_NoAdditionalAuditEntry`, `OnPostRevokeApiKeyAsync_UnknownId_NoOp`, `OnGetAsync_ApiKeysListedNewestFirst`, `OnGetExportAllAsync_ReturnsFileWithExpectedWorksheets` (now also asserts the "API Keys" sheet exists)
- Production code: `AuditTrailReview.WebApp.Pages.AdminPanel.Settings.IndexModel` (`OnPostGenerateApiKeyAsync`, `OnPostRevokeApiKeyAsync`, `LoadApiKeysAsync`, `ApiKeyCredentialAuditSnapshot`, `AddApiKeysSheet`), `AuditTrailReview.Model.ApiKeyCredential`, `AuditTrailReview.Engine.Security.ApiKeyCredentialValidator.GenerateRawKey`/`ComputeKeyHint` (TC-808), `AuditTrailReview.Engine.Security.ApiKeyProtector` (TC-806)

## Notes
- The "API" sub-tab of the Audit Trail tab (filtering `dbo.AuditTrail` to `Object == "ApiKeyCredential"`) reuses the same `LoadAuditTrailSubTabAsync` helper already covered for the other four sub-tabs in TC-703 -- no separate include/exclude test was added for it specifically, since the filtering logic itself is shared and already exercised there.
- The AdminPanel UI's "Generate" confirmation banner and per-row "Revoke" button (with its JavaScript confirm dialog) are not covered by an automated UI test.

## Status
Active
