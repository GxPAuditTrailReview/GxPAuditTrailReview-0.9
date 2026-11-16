-- Adds dbo.AuditTrail.TargetUserId so a user-account create/update/delete entry
-- (Pages/AdminPanel/User/Create|Edit|Delete.cshtml.cs) can be traced back to the specific
-- AspNetUsers row it describes. Same gap as ATR 0.25/0.26/0.27's SystemID/ScanID/SystemTypeID
-- columns -- objectName for these three handlers is the affected account's email address
-- (objectName: user.Email ?? user.Id), which is NOT a stable identifier: Edit.cshtml.cs lets an
-- admin change a user's email, so an older entry logged under the previous address would no
-- longer match a filter on the current one. [nvarchar](450) to match AspNetUsers.Id's own
-- column type/length exactly (both are ASP.NET Core Identity's default string key).
-- Deliberately a DIFFERENT column from the existing dbo.AuditTrail.UserID -- that column already
-- means something else entirely: the *acting* admin's username (AuditTrailService.BuildEntries'
-- userName parameter), not the account being changed. Nullable -- only AuditObjectType.User
-- entries populate it; every other object type leaves it NULL. Only covers entries logged going
-- forward; existing rows can't be retroactively attributed to a specific account.
-- Safe to run on an installation where the column/index already exists -- the existence checks
-- make it a no-op in that case.

USE [GxPAuditTrail]
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.AuditTrail') AND name = 'TargetUserId'
)
BEGIN
    ALTER TABLE [dbo].[AuditTrail] ADD [TargetUserId] [nvarchar](450) NULL;
END
GO

-- AdminPanel/User/Details.cshtml's "Audit trail" tab filters this table by
-- "WHERE TargetUserId = @id" on every page load, same reasoning as IX_AuditTrail_SystemID
-- (ATR 0.25), IX_AuditTrail_ScanID (ATR 0.26), and IX_AuditTrail_SystemTypeID (ATR 0.27).
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'IX_AuditTrail_TargetUserId' AND object_id = OBJECT_ID('dbo.AuditTrail')
)
BEGIN
    CREATE NONCLUSTERED INDEX [IX_AuditTrail_TargetUserId] ON [dbo].[AuditTrail]([TargetUserId] ASC)
END
GO
