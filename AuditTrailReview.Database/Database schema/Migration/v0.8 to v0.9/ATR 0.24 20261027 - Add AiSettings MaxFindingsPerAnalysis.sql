-- Adds dbo.AiSettings.MaxFindingsPerAnalysis and dbo.AiSettings.FullScanRowThreshold, which
-- together bound how much of a scan's data is sent to OpenAI for AI-assisted analysis
-- (AuditTrailReview.Engine.ScanResultService.ConvertScanResultsToJson /
-- BuildScanStatisticalSummaryJson, consumed by OpenAIService.QueryJsonWithPromptAsync). A scan
-- with FullScanRowThreshold rows or fewer still gets its entire result set sent (unchanged,
-- full independent-analysis behavior); a larger scan -- up to ~5,000,000 rows for a large
-- upload, which was never feasible to send in full regardless of any timeout -- falls back to
-- at most MaxFindingsPerAnalysis rule-engine findings (evenly sampled, not just the first ones)
-- plus a whole-scan statistical summary. See ADR-0006's amendment for the full rationale.
-- Safe to run on an installation where either column already exists -- the existence checks
-- make it a no-op in that case.

USE [GxPAuditTrail]
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.AiSettings') AND name = 'MaxFindingsPerAnalysis'
)
BEGIN
    ALTER TABLE [dbo].[AiSettings] ADD [MaxFindingsPerAnalysis] [int] NOT NULL DEFAULT (200);
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.AiSettings') AND name = 'FullScanRowThreshold'
)
BEGIN
    ALTER TABLE [dbo].[AiSettings] ADD [FullScanRowThreshold] [int] NOT NULL DEFAULT (300);
END
GO
