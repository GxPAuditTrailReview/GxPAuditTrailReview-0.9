-- Adds dbo.Scan.UserId so a scan can be traced back to the signed-in WebApp user who uploaded
-- it. [nvarchar](450) to match AspNetUsers.Id's own column type/length exactly, same convention
-- as dbo.ScanUploadJob.UserId. NULL for scans ingested via FTP or the Integration Hub API --
-- neither has a signed-in-user concept (service-account/API-key driven).
-- Safe to run on an installation where the column/index already exists -- the existence checks
-- make it a no-op in that case.

USE [GxPAuditTrail]
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.Scan') AND name = 'UserId'
)
BEGIN
    ALTER TABLE [dbo].[Scan] ADD [UserId] [nvarchar](450) NULL;
END
GO

-- Dashboard's "My Latest 5 scans" filters this table by "WHERE UserId = @userId" on every
-- page load, same reasoning as IX_AuditTrail_TargetUserId (ATR 0.28).
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'IX_Scan_UserId' AND object_id = OBJECT_ID('dbo.Scan')
)
BEGIN
    CREATE NONCLUSTERED INDEX [IX_Scan_UserId] ON [dbo].[Scan]([UserId] ASC)
END
GO
