-- Adds dbo.AiSettings.ApiKeyEncrypted and dbo.AiSettings.Model, so the OpenAI API key and the
-- chat model can be changed from the AI Analysis tab (AdminPanel/Settings/Index.cshtml) without
-- editing config files or redeploying. ApiKeyEncrypted holds ciphertext only -- see
-- AuditTrailReview.Engine.Security.ApiKeyProtector, which encrypts/decrypts it with a separate
-- master key that stays in configuration (Security:EncryptionKey, named AiApiKeyEncryptionKey
-- when this script was written, renamed once it started protecting EmailSettings.PasswordEncrypted
-- too -- see ATR 0.32), never in this database. Model is restricted to
-- Engine.OpenAIService.AllowedChatModels by application code, not by this schema.
-- Safe to run on an installation where either column already exists -- the existence checks
-- make it a no-op in that case.

USE [GxPAuditTrail]
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.AiSettings') AND name = 'ApiKeyEncrypted'
)
BEGIN
    ALTER TABLE [dbo].[AiSettings] ADD [ApiKeyEncrypted] [nvarchar](max) NULL;
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.AiSettings') AND name = 'Model'
)
BEGIN
    ALTER TABLE [dbo].[AiSettings] ADD [Model] [nvarchar](100) NOT NULL DEFAULT ('gpt-4o-mini');
END
GO
