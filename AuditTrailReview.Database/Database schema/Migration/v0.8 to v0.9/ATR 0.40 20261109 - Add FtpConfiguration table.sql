-- Adds dbo.FtpConfiguration, a singleton (one row, Id = 1 by convention) settings row for the
-- Worker Service's FTP/FTPS ingestion, edited in AdminPanel/Settings/Index.cshtml (FTP tab).
-- It replaces the appsettings.json "Ftp" section (now used only to seed this row on first start).
-- Enabled is the master switch and defaults to 0 (off). EncryptionMode: 0 = None (plain FTP),
-- 1 = Explicit FTPS, 2 = Implicit FTPS. PasswordEncrypted holds ciphertext only -- see
-- AuditTrailReview.Engine.Security.ApiKeyProtector (same master key as EmailSettings).
-- Safe to run on an installation where the table already exists -- the existence check makes it
-- a no-op in that case.

USE [GxPAuditTrail]
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'FtpConfiguration')
BEGIN
    CREATE TABLE [dbo].[FtpConfiguration](
        [Id] [int] IDENTITY(1,1) NOT NULL,
        [Enabled] [bit] NOT NULL CONSTRAINT [DF_FtpConfiguration_Enabled] DEFAULT (0),
        [Host] [nvarchar](255) NULL,
        [Port] [int] NULL,
        [EncryptionMode] [int] NOT NULL CONSTRAINT [DF_FtpConfiguration_EncryptionMode] DEFAULT (1),
        [ValidateCertificate] [bit] NOT NULL CONSTRAINT [DF_FtpConfiguration_ValidateCertificate] DEFAULT (1),
        [CertificateThumbprint] [nvarchar](128) NULL,
        [Username] [nvarchar](256) NULL,
        [PasswordEncrypted] [nvarchar](max) NULL,
        [RemoteFolder] [nvarchar](512) NOT NULL CONSTRAINT [DF_FtpConfiguration_RemoteFolder] DEFAULT ('/'),
        [ProcessedFolder] [nvarchar](256) NULL,
        [FailedFolder] [nvarchar](256) NULL,
        [PollingIntervalSeconds] [int] NOT NULL CONSTRAINT [DF_FtpConfiguration_Polling] DEFAULT (30),
        [StaleUnmovedFileThresholdMinutes] [int] NOT NULL CONSTRAINT [DF_FtpConfiguration_StaleThreshold] DEFAULT (60),
        [StaleFileCheckIntervalMinutes] [int] NOT NULL CONSTRAINT [DF_FtpConfiguration_StaleInterval] DEFAULT (60),
        [SystemId] [int] NULL,
     CONSTRAINT [PK_FtpConfiguration] PRIMARY KEY CLUSTERED
    (
        [Id] ASC
    ) ON [PRIMARY]
    ) ON [PRIMARY]
END
GO
