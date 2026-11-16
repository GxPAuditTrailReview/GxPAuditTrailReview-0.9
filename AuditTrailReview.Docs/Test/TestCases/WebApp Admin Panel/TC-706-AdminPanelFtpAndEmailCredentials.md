# TC-706 – Admin Panel Settings: FTP and Email Tabs

## Objective
Verify the "FTP" and "Email" tabs on the consolidated Application Settings page (`Pages/AdminPanel/Settings/Index.cshtml.cs`'s `IndexModel`): the FTP master switch is off by default and cannot be switched on without a host; the FTP and SMTP passwords are stored encrypted, kept when the field is left blank, cleared on request, and never written to the audit trail in clear text; and changes to the switch or user name are audit-logged.

## Preconditions
- `IndexModel` is built over an EF Core InMemory `ApplicationDbContext` with a base64 32-byte `Security:EncryptionKey` in configuration (`ApiKeyProtector`, TC-806) and a signed-in user from `PageModelTestHelpers.InitializePageContext`.

## Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | `OnGetAsync` with no `FtpConfiguration` row | FTP ingestion is shown as off |
| 2 | `OnPostSaveFtpAsync` with the switch on and no host | Rejected with a validation error; nothing is saved |
| 3 | `OnPostSaveFtpAsync` with the switch off and empty fields | Saved |
| 4 | `OnPostSaveFtpAsync` with a password | Stored in `PasswordEncrypted`, not as plaintext; the audit-trail entry does not contain it |
| 5 | `OnPostSaveFtpAsync` with a blank password field on an existing row | The stored encrypted password is kept |
| 6 | `OnPostSaveFtpAsync` toggling the switch | An audit entry records the `Enabled` change |
| 7 | `OnPostSaveEmailAsync` with an SMTP password | Stored encrypted, not plaintext |
| 8 | `OnPostSaveEmailAsync` with a blank password field | The stored encrypted password is kept |
| 9 | `OnPostSaveEmailAsync` with "clear SMTP password" checked | The stored password is removed |
| 10 | `OnPostSaveEmailAsync` changing the user name | An audit entry records the change |
| 11 | `OnPostSaveEmailAsync` changing only the password | Still produces an audit entry (without the password value) |

## Assertions
- FTP ingestion cannot be enabled without a host, and defaults to off.
- Neither password reaches `dbo.AuditTrail` in clear text, and a blank field never wipes a stored password by accident.

## Related Code
- Test classes: `AuditTrailReview.UnitTest.WebApp.AdminPanel.Settings.IndexModelTests`
- Methods: `OnGetAsync_NoFtpRow_FtpIngestionIsOffByDefault`, `OnPostSaveFtpAsync_EnabledWithoutHost_RejectedAndNothingSaved`, `OnPostSaveFtpAsync_DisabledWithEmptyFields_Saved`, `OnPostSaveFtpAsync_PasswordProvided_StoresEncryptedAndKeepsItOutOfAuditTrail`, `OnPostSaveFtpAsync_PasswordBlank_PreservesExistingPassword`, `OnPostSaveFtpAsync_EnabledToggled_AppearsInAuditEntry`, `OnPostSaveEmailAsync_PasswordProvided_StoresEncryptedNotPlaintext`, `OnPostSaveEmailAsync_PasswordInputBlank_PreservesExistingEncryptedPassword`, `OnPostSaveEmailAsync_ClearSmtpPasswordChecked_RemovesStoredPassword`, `OnPostSaveEmailAsync_UserNameChanged_AppearsInAuditEntry`, `OnPostSaveEmailAsync_PasswordChangedOnlyNothingElseChanged_StillProducesAuditEntry`
- Production code: `AuditTrailReview.WebApp.Pages.AdminPanel.Settings.IndexModel` (`OnPostSaveFtpAsync`, `OnPostSaveEmailAsync`), `AuditTrailReview.Model.FtpConfiguration`
- Related: TC-703 (audit-trail sub-tabs and the AI tab on the same page), TC-813 (how the Worker Service reads the FTP settings), TC-806

## Notes
- Not covered by an automated test: the Email tab's **Test connection** button (it needs an SMTP server), and the FTP tab's field-level validation beyond the host check.

## Status
Active
