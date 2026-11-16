-- Removes dbo.DocChunk and dbo.DocIndexStatus (the in-app documentation chatbot's indexed-chunk
-- storage and reindex-status singleton row) and dbo.AiSettings.EmbeddingDeploymentName (the
-- Azure OpenAI/Microsoft Foundry embedding deployment id that chatbot used) -- the chatbot
-- feature itself (Pages/Help/Chat, Pages/AdminPanel/DocsChatbot, Engine/AI/DocIndexer/
-- DocChatService/DocChunker/CosineSimilarity/EmbeddingCodec) has been removed from the
-- application; nothing reads these tables/column any more.
-- Neither table has any foreign key relationship to drop first. Safe to run on an installation
-- where any of these objects is already gone -- the existence checks make it a no-op in that case.

USE [GxPAuditTrail]
GO

IF OBJECT_ID('dbo.DocChunk', 'U') IS NOT NULL
BEGIN
    DROP TABLE [dbo].[DocChunk];
END
GO

IF OBJECT_ID('dbo.DocIndexStatus', 'U') IS NOT NULL
BEGIN
    DROP TABLE [dbo].[DocIndexStatus];
END
GO

IF EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.AiSettings') AND name = 'EmbeddingDeploymentName'
)
BEGIN
    ALTER TABLE [dbo].[AiSettings] DROP COLUMN [EmbeddingDeploymentName];
END
GO
