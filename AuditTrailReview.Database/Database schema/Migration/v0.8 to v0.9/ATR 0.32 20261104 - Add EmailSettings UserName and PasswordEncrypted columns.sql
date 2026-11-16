-- Adds dbo.EmailSettings.UserName and dbo.EmailSettings.PasswordEncrypted, so the SMTP account
-- (previously only configurable via appsettings.json's SmtpSettings:UserName/Password) can also
-- be changed from the Email tab (AdminPanel/Settings/Index.cshtml) without editing config files
-- or redeploying. PasswordEncrypted holds ciphertext only -- see
-- AuditTrailReview.Engine.Security.ApiKeyProtector, which encrypts/decrypts it with the same
-- master key already used for AiSettings.ApiKeyEncrypted (Security:EncryptionKey, renamed from
-- Security:AiApiKeyEncryptionKey now that it protects a second secret too), never stored in this
-- database. UserName is plain text (not secret on its own).
-- Safe to run on an installation where either column already exists -- the existence checks
-- make it a no-op in that case.

USE [GxPAuditTrail]
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.EmailSettings') AND name = 'UserName'
)
BEGIN
    ALTER TABLE [dbo].[EmailSettings] ADD [UserName] [nvarchar](256) NULL;
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.EmailSettings') AND name = 'PasswordEncrypted'
)
BEGIN
    ALTER TABLE [dbo].[EmailSettings] ADD [PasswordEncrypted] [nvarchar](max) NULL;
END
GO
