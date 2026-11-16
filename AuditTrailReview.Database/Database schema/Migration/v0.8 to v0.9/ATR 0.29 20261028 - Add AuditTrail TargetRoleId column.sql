-- Adds dbo.AuditTrail.TargetRoleId so a role create/update/delete entry
-- (Pages/AdminPanel/Role/Create|Edit|Delete.cshtml.cs) can be traced back to the specific
-- AspNetRoles row it describes. Same gap as ATR 0.28's AuditTrail.TargetUserId -- objectName for
-- these three handlers is the role's own Name (objectName: RoleName ?? id / roleName /
-- role.Name ?? id), which is NOT a stable identifier: Edit.cshtml.cs lets an admin rename a role,
-- so an older entry logged under the previous name would no longer match a filter on the current
-- one. [nvarchar](450) to match AspNetRoles.Id's own column type/length exactly (both are
-- ASP.NET Core Identity's default string key).
-- Nullable -- only AuditObjectType.AdminPanel entries logged by these three Role handlers
-- populate it; every other AdminPanel entry (e.g. AdminPanel/DocsChatbot, if any were ever added
-- under this same ObjectType) leaves it NULL. Only covers entries logged going forward; existing
-- rows can't be retroactively attributed to a specific role.
-- Safe to run on an installation where the column/index already exists -- the existence checks
-- make it a no-op in that case.

USE [GxPAuditTrail]
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.AuditTrail') AND name = 'TargetRoleId'
)
BEGIN
    ALTER TABLE [dbo].[AuditTrail] ADD [TargetRoleId] [nvarchar](450) NULL;
END
GO

-- AdminPanel/Role/Details.cshtml's "Audit trail" tab filters this table by
-- "WHERE TargetRoleId = @id" on every page load, same reasoning as IX_AuditTrail_TargetUserId
-- (ATR 0.28).
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'IX_AuditTrail_TargetRoleId' AND object_id = OBJECT_ID('dbo.AuditTrail')
)
BEGIN
    CREATE NONCLUSTERED INDEX [IX_AuditTrail_TargetRoleId] ON [dbo].[AuditTrail]([TargetRoleId] ASC)
END
GO
