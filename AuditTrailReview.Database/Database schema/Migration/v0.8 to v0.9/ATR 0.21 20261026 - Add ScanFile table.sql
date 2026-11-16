-- Adds dbo.ScanFile, which holds the original bytes of the file behind each Scan so it can be
-- downloaded again from Pages/Scan/Details.cshtml (see AuditTrailReview.Model.ScanFile's own
-- remarks). Written only going forward -- scans processed before this script runs simply have no
-- matching row, which Details.cshtml treats as "original file not available" rather than an error.
-- No FOREIGN KEY constraint to dbo.Scan, consistent with how this schema already references Scan
-- from dbo.ScanResult and dbo.ScanUploadJob: by convention only, enforced in code, not in the DB.
-- Safe to run on an installation where the table already exists -- the existence check makes it a
-- no-op in that case.

USE [GxPAuditTrail]
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'ScanFile')
BEGIN
    CREATE TABLE [dbo].[ScanFile](
        [Id] [int] IDENTITY(1,1) NOT NULL,
        [ScanId] [int] NOT NULL,
        [Content] [varbinary](max) NULL,
        [UploadedAtUtc] [datetime2](7) NOT NULL,
     CONSTRAINT [PK_ScanFile] PRIMARY KEY CLUSTERED
    (
        [Id] ASC
    ) ON [PRIMARY]
    ) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'IX_ScanFile_ScanId' AND object_id = OBJECT_ID('dbo.ScanFile')
)
BEGIN
    CREATE UNIQUE INDEX [IX_ScanFile_ScanId] ON [dbo].[ScanFile]([ScanId] ASC)
END
GO
