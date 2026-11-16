# TC-813 – FTP Settings Resolution and Certificate Acceptance

## Objective
Verify `FtpSettingsResolver`, which turns the one-row `dbo.FtpConfiguration` table into the effective settings the Worker Service uses on every polling cycle: ingestion is off unless explicitly enabled, the stored password is decrypted, the port defaults depend on the encryption mode, and the TLS certificate decision honours chain validation and an optional pinned thumbprint.

## Preconditions
- No database or FTP server is needed. `Resolve` is called with a `FtpConfiguration` instance (or `null`) and an in-memory `IConfiguration`.
- For the password case, `Security:EncryptionKey` is a base64 32-byte key and the password is encrypted with `ApiKeyProtector` (TC-806).

## Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | `Resolve(null, config)` (no row in the table) | `Enabled` is false |
| 2 | `Resolve(new FtpConfiguration(), config)` (a row with defaults) | `Enabled` is false: the master switch is off by default |
| 3 | `Resolve` a row with `Enabled`, host `" ftp.example.com "`, user, encrypted password and `SystemId = 7` | `Enabled` true, host trimmed to `ftp.example.com`, password decrypted to the original value, `SystemId` 7 |
| 4 | `EffectivePort` for no port + None, no port + Explicit, no port + Implicit, and 2121 + Implicit | 21, 21, 990 and 2121: a configured port wins, otherwise 990 for implicit FTPS and 21 for the rest |
| 5 | `NormalizeThumbprint("aa:bb cc")`, and `"  "` | `AABBCC`; `null` for a blank value |
| 6 | `IsCertificateAcceptable` with validation on and a valid chain | Accepted |
| 7 | Same with validation on and an invalid chain | Rejected |
| 8 | Same with validation off and an invalid chain | Accepted |
| 9 | A pinned thumbprint that matches the server certificate, with an invalid chain | Accepted, even though the chain is bad |
| 10 | A pinned thumbprint that does not match, with validation off | Rejected, even though validation is off |

## Assertions
- A missing row and a default row both mean "FTP ingestion off".
- The resolved password is the decrypted plaintext; the ciphertext never leaves the resolver.
- A pinned thumbprint overrides the chain check in both directions.

## Related Code
- Test classes: `AuditTrailReview.UnitTest.Engine.FtpSettingsResolverTests`
- Methods: `Resolve_NoRow_ReturnsDisabled`, `Resolve_NewRowDefaults_IsDisabled`, `Resolve_EncryptedPassword_ReturnsDecryptedPassword`, `EffectivePort_ConfiguredOrDefault_ReturnsExpected` (4 data rows), `NormalizeThumbprint_SeparatorsAndCase_AreStripped`, `IsCertificateAcceptable_ValidChain_Accepted`, `IsCertificateAcceptable_InvalidChainValidationOn_Rejected`, `IsCertificateAcceptable_InvalidChainValidationOff_Accepted`, `IsCertificateAcceptable_PinnedThumbprintMatches_AcceptedEvenWithBadChain`, `IsCertificateAcceptable_PinnedThumbprintMismatch_RejectedEvenWithValidationOff`
- Production code: `AuditTrailReview.Engine.Services.FtpSettingsResolver`, `AuditTrailReview.Model.FtpConfiguration`
- Related: TC-706 (the FTP tab that writes the settings), TC-806 (password encryption), TC-401 (manual FTP polling script)

## Notes
- Not covered by an automated test: the live FTP/FTPS connection, `FtpClientFactory`, and the polling loop. They are exercised only by the manual script TC-401.

## Status
Active
