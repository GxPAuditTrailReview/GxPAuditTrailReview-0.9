-- Drops dbo.EngineSettings.ReasonMinAlphabeticChars, a leftover from the retired A5 rule
-- ("reason must contain alphabetic characters"). EngineSettings.cs and Program.cs's seeding
-- comments already describe this column as removed by the sixth rule-management round's
-- "ATR 0.19 ... - EngineSettings Drop Retired-Rule Settings.sql", but the full-schema
-- snapshot this script sits on top of (ATR 0.8 20261020) still carries the column, and
-- nothing in the current EngineSettings model class reads it. This script brings the live
-- schema back in line with the code. Safe to run on an installation where the column is
-- already gone -- the existence checks make it a no-op in that case.

USE [GxPAuditTrail]
GO

IF EXISTS (
    SELECT 1 FROM sys.default_constraints
    WHERE name = 'DF_EngineSettings_ReasonMinAlphabeticChars'
)
BEGIN
    ALTER TABLE [dbo].[EngineSettings] DROP CONSTRAINT [DF_EngineSettings_ReasonMinAlphabeticChars];
END
GO

IF EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.EngineSettings') AND name = 'ReasonMinAlphabeticChars'
)
BEGIN
    ALTER TABLE [dbo].[EngineSettings] DROP COLUMN [ReasonMinAlphabeticChars];
END
GO
