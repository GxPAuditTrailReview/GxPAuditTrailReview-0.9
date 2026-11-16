-- Adds dbo.ApiKeyCredential, replacing IntegrationHub.API's old ApiKeyAuth:Keys static config
-- list with named, DB-stored keys managed from AdminPanel/Settings/Index.cshtml's "API" tab.
-- EncryptedKey holds ciphertext only (AuditTrailReview.Engine.Security.ApiKeyProtector, same
-- master key -- Security:EncryptionKey -- as AiSettings.ApiKeyEncrypted/
-- EmailSettings.PasswordEncrypted). Not a singleton table: any number of rows may exist. A key
-- is "active" while RevokedUtc is NULL; revoking never deletes the row.
-- Safe to run on an installation where the table already exists -- the existence check makes it
-- a no-op in that case.

USE [GxPAuditTrail]
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'ApiKeyCredential')
BEGIN
    CREATE TABLE [dbo].[ApiKeyCredential](
        [Id] [int] IDENTITY(1,1) NOT NULL,
        [Name] [nvarchar](200) NOT NULL,
        [EncryptedKey] [nvarchar](max) NOT NULL,
        [KeyHint] [nvarchar](8) NOT NULL,
        [CreatedUtc] [datetime2](7) NOT NULL,
        [RevokedUtc] [datetime2](7) NULL,
     CONSTRAINT [PK_ApiKeyCredential] PRIMARY KEY CLUSTERED
    (
        [Id] ASC
    ) ON [PRIMARY]
    ) ON [PRIMARY]
END
GO
