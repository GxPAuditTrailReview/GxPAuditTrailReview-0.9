-- Adds dbo.AiSettings.Provider/Endpoint/DeploymentName/EmbeddingDeploymentName/ApiVersion, so the
-- AI Analysis tab (AdminPanel/Settings/Index.cshtml) can point Engine.OpenAIService at Azure OpenAI
-- or Microsoft Foundry instead of just api.openai.com -- see
-- Engine.OpenAIService.BuildChatUrl/BuildEmbeddingsUrl for how these are used, and
-- AuditTrailReview.Model.AiProvider for what each Provider value means.
-- Provider defaults to 0 (AiProvider.OpenAI) so every existing row keeps today's behavior with no
-- further action needed. The other four columns are nullable: they're only required (enforced by
-- AiSettings.Validate, application-side) once Provider is changed away from OpenAI.
-- Safe to run on an installation where any of these columns already exist -- the existence checks
-- make it a no-op in that case.

USE [GxPAuditTrail]
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.AiSettings') AND name = 'Provider'
)
BEGIN
    ALTER TABLE [dbo].[AiSettings] ADD [Provider] [int] NOT NULL DEFAULT (0);
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.AiSettings') AND name = 'Endpoint'
)
BEGIN
    ALTER TABLE [dbo].[AiSettings] ADD [Endpoint] [nvarchar](500) NULL;
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.AiSettings') AND name = 'DeploymentName'
)
BEGIN
    ALTER TABLE [dbo].[AiSettings] ADD [DeploymentName] [nvarchar](200) NULL;
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.AiSettings') AND name = 'EmbeddingDeploymentName'
)
BEGIN
    ALTER TABLE [dbo].[AiSettings] ADD [EmbeddingDeploymentName] [nvarchar](200) NULL;
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.AiSettings') AND name = 'ApiVersion'
)
BEGIN
    ALTER TABLE [dbo].[AiSettings] ADD [ApiVersion] [nvarchar](50) NULL;
END
GO
