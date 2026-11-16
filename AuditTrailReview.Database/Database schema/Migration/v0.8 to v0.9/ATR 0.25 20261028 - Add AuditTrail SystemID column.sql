-- Adds dbo.AuditTrail.SystemID so a System create/update/delete entry (Pages/System/Create.cshtml.cs,
-- Edit.cshtml.cs, Delete.cshtml.cs) can be traced back to the specific SystemATR row it describes.
-- Before this column existed every System-type entry was logged with the same literal
-- objectName ("SystemATR") and no per-system identifier survived save (AuditTrailService's
-- existing UniqueIdentifier parameter is always overwritten with the log row's own Id after
-- persist -- see LogServiceTests.LogCreateAsync_ExplicitUniqueIdentifier_IsUsedInstead), so there
-- was no way to filter the audit trail log down to one system. Nullable -- only AuditObjectType.System
-- entries populate it; every other object type leaves it NULL. Only covers entries logged going
-- forward; existing rows can't be retroactively attributed to a system.
-- Safe to run on an installation where the column/index already exists -- the existence checks
-- make it a no-op in that case.

USE [GxPAuditTrail]
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.AuditTrail') AND name = 'SystemID'
)
BEGIN
    ALTER TABLE [dbo].[AuditTrail] ADD [SystemID] [int] NULL;
END
GO

-- System/Details.cshtml's "Audit trail" tab filters this table by "WHERE SystemID = @id" on every
-- page load, same reasoning as IX_ScanResult_ScanID (ATR 0.22).
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'IX_AuditTrail_SystemID' AND object_id = OBJECT_ID('dbo.AuditTrail')
)
BEGIN
    CREATE NONCLUSTERED INDEX [IX_AuditTrail_SystemID] ON [dbo].[AuditTrail]([SystemID] ASC)
END
GO
