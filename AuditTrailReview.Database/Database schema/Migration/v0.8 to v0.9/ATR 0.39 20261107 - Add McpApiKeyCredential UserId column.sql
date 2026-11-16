-- Adds dbo.McpApiKeyCredential.UserId so an MCP key can be issued to a specific WebApp user and
-- MCP access attributed to a person (ADR-0010). NULL = shared/service key (all existing rows).
-- Holds AspNetUsers.Id; deliberately NOT a foreign key so deleting a user never blocks or
-- cascades to the key row (revoked keys are kept for history).
-- Safe to re-run: the existence check makes it a no-op once the column exists.

USE [GxPAuditTrail]
GO

IF COL_LENGTH('dbo.McpApiKeyCredential', 'UserId') IS NULL
BEGIN
    ALTER TABLE [dbo].[McpApiKeyCredential] ADD [UserId] [nvarchar](450) NULL
END
GO
