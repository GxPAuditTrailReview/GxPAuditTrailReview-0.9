# TC-705 – WebApp Admin Panel: Application Settings MCP Key Management

## Objective
Verify the "MCP" tab on the consolidated Application Settings page (`Pages/AdminPanel/Settings/Index.cshtml.cs`'s `IndexModel`): `OnPostGenerateMcpApiKeyAsync`, `OnPostRevokeMcpApiKeyAsync`, and the key listing loaded by `LoadMcpApiKeysAsync`. This tab manages `dbo.McpApiKeyCredential`, the named, independently revocable API keys `AuditTrailReview.McpServer`'s `McpApiKeyMiddleware` validates against (see TC-807), replacing the old static `McpAuth:Keys` config list. It is structurally identical to the "API" tab (TC-704) -- same generate-once/revoke/list UI and handler shape -- but deliberately writes to a completely separate table, so a key generated here can never authenticate `IntegrationHub.API`'s own endpoints, and a key generated on the "API" tab can never authenticate the MCP server.

## Preconditions
- `IndexModel` is constructed with an EF Core InMemory `ApplicationDbContext`, a real `AuditTrailService` (TC-801), and (for `OnPostGenerateMcpApiKeyAsync`, which calls `ApiKeyProtector.Encrypt`) an `IConfiguration` seeded with a valid base64 32-byte `Security:EncryptionKey` -- same `CreateConfigurationWithEncryptionKey()` helper the API/AI/Email tab tests use (TC-704, TC-703, TC-806).
- `PageModelTestHelpers.InitializePageContext` wires up a signed-in user and `TempData`.

## Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | `OnPostGenerateMcpApiKeyAsync` with a non-blank `NewMcpApiKeyName` | A new `dbo.McpApiKeyCredential` row is created with that `Name`, a non-empty `EncryptedKey`, and `RevokedUtc == null`; the raw (plaintext) key is stashed in `TempData["GeneratedMcpApiKeyPlaintext"]` and decrypts back to the exact same value via `ApiKeyProtector.Decrypt`; the row's `KeyHint` is the raw key's last 4 characters |
| 2 | Same as step 1, then inspect every `dbo.AuditTrail` entry for `Object == "McpApiKeyCredential"` from that call | At least one entry is logged, none with `Field == "EncryptedKey"`, and no entry's `OldValue`/`NewValue` contains the raw plaintext key |
| 3 | `OnPostGenerateMcpApiKeyAsync` with a blank/whitespace-only `NewMcpApiKeyName` | A `RedirectToPageResult` is returned (validation failure) and no row is added to `dbo.McpApiKeyCredential` |
| 4 | `OnPostRevokeMcpApiKeyAsync` on an existing active key's `Id` | `RevokedUtc` is set (non-null) on that row; the row count in `dbo.McpApiKeyCredential` is unchanged (revoke never deletes); a `dbo.AuditTrail` entry with `Field == "RevokedUtc"` is logged |
| 5 | `OnPostRevokeMcpApiKeyAsync` on a key that is already revoked | No-op: no additional `dbo.AuditTrail` entry is logged |
| 6 | `OnPostRevokeMcpApiKeyAsync` with an `Id` that does not exist | No-op: `RedirectToPageResult` returned, no row created or changed |
| 7 | `OnGetAsync` with two existing `McpApiKeyCredential` rows of different `CreatedUtc` | `IndexModel.McpApiKeys` lists both, newest `CreatedUtc` first |
| 8 | `OnPostGenerateApiKeyAsync` (the "API" tab's own handler) | Creates a row in `dbo.ApiKeyCredential` only -- `dbo.McpApiKeyCredential` remains empty, confirming the two tabs/tables never cross-write |
| 9 | `OnPostGenerateMcpApiKeyAsync` with `NewMcpApiKeyUserId` set to an existing user | The new row stores that `UserId` (key issued to a person) |
| 10 | Same with a user id that does not exist | Rejected: error in `TempData`, no key row created |
| 11 | Same with no user selected | Shared key: `UserId` is null |

## Assertions
- The raw MCP key is never persisted anywhere -- only `ApiKeyProtector`-encrypted ciphertext (`EncryptedKey`) and the non-secret `KeyHint` are stored -- and is surfaced to the admin exactly once, via `TempData`, immediately after generation.
- `McpApiKeyCredentialAuditSnapshot` (the private DTO `OnPostGenerateMcpApiKeyAsync`/`OnPostRevokeMcpApiKeyAsync` pass to `AuditTrailService`) has no `EncryptedKey` or `Id` property at all, the same structural-exclusion technique `ApiKeyCredentialAuditSnapshot`/`AiSettingsAuditSnapshot` use (TC-704, TC-703, TC-806).
- Revoking a key is a soft update (`RevokedUtc` set), never a row deletion, so its audit history (including the original "generated" entry) stays intact.
- `NewMcpApiKeyName` is required server-side before a key is generated.
- `McpApiKeys` is ordered newest-first with no pagination, same convention as `ApiKeys` (TC-704).
- Generating or revoking a key on one tab never creates, modifies, or reads a row in the other tab's table.

## Related Code
- Test class: `AuditTrailReview.UnitTest.WebApp.AdminPanel.Settings.IndexModelTests`
- Methods: `OnPostGenerateMcpApiKeyAsync_ValidName_CreatesEncryptedRowAndStashesPlaintextOnce`, `OnPostGenerateMcpApiKeyAsync_ExistingUserSelected_StoresUserId`, `OnPostGenerateMcpApiKeyAsync_UnknownUserSelected_RejectsAndCreatesNoKey`, `OnPostGenerateMcpApiKeyAsync_NoUserSelected_CreatesSharedKeyWithNullUserId`, `OnPostGenerateMcpApiKeyAsync_BlankName_DoesNotCreateRow`, `OnPostRevokeMcpApiKeyAsync_ActiveKey_SetsRevokedUtcAndKeepsRow`, `OnPostRevokeMcpApiKeyAsync_AlreadyRevokedKey_NoAdditionalAuditEntry`, `OnPostRevokeMcpApiKeyAsync_UnknownId_NoOp`, `OnGetAsync_McpApiKeysListedNewestFirst`, `OnPostGenerateApiKeyAsync_DoesNotCreateMcpApiKeyCredentialRow`, `OnGetExportAllAsync_ReturnsFileWithExpectedWorksheets` (now also asserts the "MCP Keys" sheet exists)
- Production code: `AuditTrailReview.WebApp.Pages.AdminPanel.Settings.IndexModel` (`OnPostGenerateMcpApiKeyAsync`, `OnPostRevokeMcpApiKeyAsync`, `LoadMcpApiKeysAsync`, `McpApiKeyCredentialAuditSnapshot`, `AddApiKeysSheet`), `AuditTrailReview.Model.McpApiKeyCredential`, `AuditTrailReview.Engine.Security.ApiKeyCredentialValidator.GenerateRawKey`/`ComputeKeyHint` (TC-808), `AuditTrailReview.Engine.Security.ApiKeyProtector` (TC-806)

## Notes
- The "MCP" sub-tab of the Audit Trail tab (filtering `dbo.AuditTrail` to `Object == "McpApiKeyCredential"`) reuses the same `LoadAuditTrailSubTabAsync` helper already covered for the other five sub-tabs in TC-703/TC-704 -- no separate include/exclude test was added for it specifically, since the filtering logic itself is shared and already exercised there.
- The "Issued to user" dropdown markup and the User column are not UI-tested; the handler behaviour behind them is covered (steps 9-11).
- The AdminPanel UI's "Generate" confirmation banner and per-row "Revoke" button (with its JavaScript confirm dialog) are not covered by an automated UI test.

## Status
Active
