-- Adds dbo.AuditTrail.SystemTypeID so a System Type create/update/delete entry
-- (Pages/ConfigPanel/SystemType/Create.cshtml.cs, Edit.cshtml.cs, Delete.cshtml.cs) can be traced
-- back to the specific SystemType row it describes. Same gap as ATR 0.25's AuditTrail.SystemID
-- and ATR 0.26's AuditTrail.ScanID: these three handlers log with AuditObjectType.ConfigPanel and
-- a constant objectName ("SystemType"), which is also the exact same ObjectType value
-- Pages/ConfigPanel/RuleBasedReviewEngine.cshtml.cs logs rule-enablement changes under -- so
-- without a dedicated column, there was no way to even isolate System Type entries from rule
-- entries, let alone filter down to one System Type. Nullable -- only entries logged by the
-- System Type pages above populate it; every other ConfigPanel entry (including rule enablement
-- changes) leaves it NULL. Only covers entries logged going forward; existing rows can't be
-- retroactively attributed to a System Type.
-- Safe to run on an installation where the column/index already exists -- the existence checks
-- make it a no-op in that case.

USE [GxPAuditTrail]
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.AuditTrail') AND name = 'SystemTypeID'
)
BEGIN
    ALTER TABLE [dbo].[AuditTrail] ADD [SystemTypeID] [int] NULL;
END
GO

-- ConfigPanel/SystemType/Details.cshtml's "Audit trail" tab filters this table by
-- "WHERE SystemTypeID = @id" on every page load, same reasoning as IX_AuditTrail_SystemID
-- (ATR 0.25) and IX_AuditTrail_ScanID (ATR 0.26).
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'IX_AuditTrail_SystemTypeID' AND object_id = OBJECT_ID('dbo.AuditTrail')
)
BEGIN
    CREATE NONCLUSTERED INDEX [IX_AuditTrail_SystemTypeID] ON [dbo].[AuditTrail]([SystemTypeID] ASC)
END
GO
