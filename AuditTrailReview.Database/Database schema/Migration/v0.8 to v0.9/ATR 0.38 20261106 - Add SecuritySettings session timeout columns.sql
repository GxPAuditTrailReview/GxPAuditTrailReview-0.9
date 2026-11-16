-- Adds dbo.SecuritySettings.IdleTimeoutMinutes, AbsoluteSessionHours and SessionWarningMinutes,
-- the session policy edited on AdminPanel/Settings' Security tab: how long a signed-in session may
-- stay idle (sliding expiration), the absolute maximum session length regardless of activity, and
-- how many minutes before the idle timeout the browser warns the user (Annex 11 s.12 / Part 11
-- controls for open sessions). Existing rows get the same defaults the application uses
-- (20 minutes / 10 hours / 2 minutes), so behavior is defined as soon as this script has run.
-- Safe to run on an installation where any column already exists -- the existence checks make it
-- a no-op in that case.

USE [GxPAuditTrail]
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.SecuritySettings') AND name = 'IdleTimeoutMinutes'
)
BEGIN
    ALTER TABLE [dbo].[SecuritySettings] ADD [IdleTimeoutMinutes] [int] NOT NULL
        CONSTRAINT [DF_SecuritySettings_IdleTimeoutMinutes] DEFAULT (20);
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.SecuritySettings') AND name = 'AbsoluteSessionHours'
)
BEGIN
    ALTER TABLE [dbo].[SecuritySettings] ADD [AbsoluteSessionHours] [int] NOT NULL
        CONSTRAINT [DF_SecuritySettings_AbsoluteSessionHours] DEFAULT (10);
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.SecuritySettings') AND name = 'SessionWarningMinutes'
)
BEGIN
    ALTER TABLE [dbo].[SecuritySettings] ADD [SessionWarningMinutes] [int] NOT NULL
        CONSTRAINT [DF_SecuritySettings_SessionWarningMinutes] DEFAULT (2);
END
GO
