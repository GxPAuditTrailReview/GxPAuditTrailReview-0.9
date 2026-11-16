-- dbo.ScanResult has never had an index on ScanID -- only its own identity PK (Id). Every read of
-- Pages/Scan/Details.cshtml (scan results tab, potential findings tab, the unreviewed-findings
-- check, the AI-analysis poll endpoint, and the Excel export) filters this table by
-- "WHERE ScanID = @id", so each of those was forcing a full clustered-index scan of the ENTIRE
-- ScanResult table -- every scan's rows, not just the one being viewed -- on every single page
-- load, regardless of how small that one scan is. With several of the performance-test files
-- (up to 5,000,000 records) already ingested as real scans in this database, that full scan was
-- the actual cause of a flat ~25-30 second load time on Scan/Details observed for scans with as
-- few as 37 rows (TC-P-001's own numbers never moved after other fixes because the bottleneck was
-- never in ScanModel/ScanProcessingService at all).
-- Not UNIQUE -- a scan's results all share its ScanID. Safe to run on an installation where the
-- index already exists -- the existence check makes it a no-op in that case.

USE [GxPAuditTrail]
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'IX_ScanResult_ScanID' AND object_id = OBJECT_ID('dbo.ScanResult')
)
BEGIN
    CREATE NONCLUSTERED INDEX [IX_ScanResult_ScanID] ON [dbo].[ScanResult]([ScanID] ASC)
END
GO

-- dbo.ScanResultAI is queried by the same Details page (its AI Analysis tab) and polled
-- repeatedly by OnGetAiAnalysisAsync while a background AI job is pending -- much smaller per
-- scan, but the same missing-index gap, so fixed alongside while touching this page's performance.
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'IX_ScanResultAI_ScanID' AND object_id = OBJECT_ID('dbo.ScanResultAI')
)
BEGIN
    CREATE NONCLUSTERED INDEX [IX_ScanResultAI_ScanID] ON [dbo].[ScanResultAI]([ScanID] ASC)
END
GO
