-- Adds dbo.GeneralSettings, a singleton (one row, Id = 1 by convention) settings row that
-- controls how every UTC date/time stored in the system is displayed: DateTimeFormat (a .NET
-- custom format string, validated against a fixed dropdown in
-- AdminPanel/Settings/EditGeneral.cshtml.cs, never free text) and DisplayTimeZone (an IANA
-- time zone ID, nullable -- null/unresolvable falls back to UTC). Independent of
-- dbo.EngineSettings.WorkdayTimeZone, which only controls the [C3] working-hours rule.
-- Safe to run on an installation where the table already exists -- the existence check makes it
-- a no-op in that case.

USE [GxPAuditTrail]
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'GeneralSettings')
BEGIN
    CREATE TABLE [dbo].[GeneralSettings](
        [Id] [int] IDENTITY(1,1) NOT NULL,
        [DateTimeFormat] [nvarchar](50) NOT NULL,
        [DisplayTimeZone] [nvarchar](100) NULL,
     CONSTRAINT [PK_GeneralSettings] PRIMARY KEY CLUSTERED
    (
        [Id] ASC
    ) ON [PRIMARY]
    ) ON [PRIMARY]
END
GO
