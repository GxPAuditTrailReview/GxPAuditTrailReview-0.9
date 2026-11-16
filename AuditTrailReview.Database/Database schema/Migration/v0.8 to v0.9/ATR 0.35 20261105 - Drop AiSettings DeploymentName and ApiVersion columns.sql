-- Removes dbo.AiSettings.DeploymentName and dbo.AiSettings.ApiVersion -- the separate Azure
-- OpenAI/Microsoft Foundry deployment id and REST "api-version" query parameter the AI Analysis
-- tab (AdminPanel/Settings/Index.cshtml) used to require. Azure OpenAI/Microsoft Foundry now use
-- the same OpenAI-compatible chat/completions URL shape as AiProvider.OpenAI (see
-- Engine.OpenAIService.BuildChatUrl): the admin's Endpoint is expected to already be the
-- resource's full base URL (e.g. https://my-resource.openai.azure.com/openai/v1), and
-- AiSettings.Model travels in the request body as the deployment/model id, so neither column is
-- read anywhere any more.
-- Safe to run on an installation where either column is already gone -- the existence checks make
-- it a no-op in that case.

USE [GxPAuditTrail]
GO

IF EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.AiSettings') AND name = 'DeploymentName'
)
BEGIN
    ALTER TABLE [dbo].[AiSettings] DROP COLUMN [DeploymentName];
END
GO

IF EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.AiSettings') AND name = 'ApiVersion'
)
BEGIN
    ALTER TABLE [dbo].[AiSettings] DROP COLUMN [ApiVersion];
END
GO
