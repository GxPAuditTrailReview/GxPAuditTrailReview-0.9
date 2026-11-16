-- Adds dbo.AuditTrail.ScanID so a Scan-related audit entry (finding review, system reassignment,
-- original file download, sign & close, reopen -- all logged by Pages/Scan/Details.cshtml.cs)
-- can be traced back to the specific Scan row it describes. Same reasoning and same gap as
-- ATR 0.25's AuditTrail.SystemID: objectName was only ever a formatted display string (e.g.
-- "Scan #20093"), never a queryable column, so there was no way to filter the audit trail log
-- down to one scan. Nullable -- only AuditObjectType.Scan entries populate it; every other
-- object type leaves it NULL. Only covers entries logged going forward; existing rows can't be
-- retroactively attributed to a scan.
-- Safe to run on an installation where the column/index already exists -- the existence checks
-- make it a no-op in that case.

USE [GxPAuditTrail]
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.AuditTrail') AND name = 'ScanID'
)
BEGIN
    ALTER TABLE [dbo].[AuditTrail] ADD [ScanID] [int] NULL;
END
GO

-- Scan/Details.cshtml's "Audit trail" tab filters this table by "WHERE ScanID = @id" on every
-- page load, same reasoning as IX_AuditTrail_SystemID (ATR 0.25).
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'IX_AuditTrail_ScanID' AND object_id = OBJECT_ID('dbo.AuditTrail')
)
BEGIN
    CREATE NONCLUSTERED INDEX [IX_AuditTrail_ScanID] ON [dbo].[AuditTrail]([ScanID] ASC)
END
GO
