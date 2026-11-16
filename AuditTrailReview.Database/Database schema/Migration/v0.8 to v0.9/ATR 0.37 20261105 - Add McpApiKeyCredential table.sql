-- Adds dbo.McpApiKeyCredential, replacing the MCP server's old McpAuth:Keys static config list
-- with named, DB-stored keys managed from AdminPanel/Settings/Index.cshtml's "MCP" tab.
-- Deliberately a separate table from dbo.ApiKeyCredential (IntegrationHub.API's own keys, ATR
-- 0.36) -- the two key pools are disjoint on purpose, so a key issued for one service can never
-- authenticate the other. EncryptedKey holds ciphertext only
-- (AuditTrailReview.Engine.Security.ApiKeyProtector, same master key -- Security:EncryptionKey --
-- as AiSettings.ApiKeyEncrypted/EmailSettings.PasswordEncrypted/ApiKeyCredential.EncryptedKey).
-- Not a singleton table: any number of rows may exist. A key is "active" while RevokedUtc is
-- NULL; revoking never deletes the row.
-- Safe to run on an installation where the table already exists -- the existence check makes it
-- a no-op in that case.

USE [GxPAuditTrail]
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'McpApiKeyCredential')
BEGIN
    CREATE TABLE [dbo].[McpApiKeyCredential](
        [Id] [int] IDENTITY(1,1) NOT NULL,
        [Name] [nvarchar](200) NOT NULL,
        [EncryptedKey] [nvarchar](max) NOT NULL,
        [KeyHint] [nvarchar](8) NOT NULL,
        [CreatedUtc] [datetime2](7) NOT NULL,
        [RevokedUtc] [datetime2](7) NULL,
     CONSTRAINT [PK_McpApiKeyCredential] PRIMARY KEY CLUSTERED
    (
        [Id] ASC
    ) ON [PRIMARY]
    ) ON [PRIMARY]
END
GO
