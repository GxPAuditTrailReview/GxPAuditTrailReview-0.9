# Architecture Decision Records (ADR)

This folder holds Architecture Decision Records for **GxPAuditTrailReview**. An ADR captures one significant architectural decision, the context that drove it, and its consequences, so future contributors (and validation/QA reviewers) can understand *why* the system is built the way it is, not just *what* it does.

## How these were produced

ADRs 0001 to 0007 were **reconstructed retroactively** from the v0.6 codebase (WebApp, Engine, Database, Model, Integration Hub API, Connector/Worker Service, Console, and test projects) rather than transcribed from original design discussions, which weren't captured at the time. Each one documents the decision as it is actually implemented today, including known trade-offs and open follow-ups discovered during code review, cross-referenced against `3 Functional & Configuration Specification.md`, `3 Functional & Configuration Specification.md`, and `PRD-Product Requirements Document.md` in this docs set. Going forward, new architectural decisions should get a new ADR at decision time, not after the fact. ADRs 0008 to 0009 were written after the fact too, shortly after the work shipped in v0.8.

## Index

| # | Title | Status |
|---|---|---|
| [0000](0000-high-level-architecture.md) | High-Level Architecture Overview | Living document |
| [0001](0001-modular-multi-project-solution.md) | Modular multi-project .NET solution | Accepted |
| [0002](0002-shared-database-across-components.md) | Single shared SQL Server database across all components | Accepted |
| [0003](0003-shared-rule-engine-library.md) | Shared rule engine library reused by all ingestion paths | Accepted |
| [0004](0004-raw-sql-schema-scripts.md) | Database schema managed via raw SQL scripts, not EF Core migrations | Accepted (contested; see Consequences) |
| [0005](0005-integration-hub-ftp-and-api.md) | Integration Hub as two ingestion paths: FTP-polling Worker Service and API-key-authenticated HTTP API | Accepted |
| [0006](0006-automatic-ai-analysis-on-manual-upload.md) | Automatic AI-assisted analysis on manual web uploads | Accepted (originally synchronous and contested; fixed to run asynchronously; see history in the ADR) |
| [0007](0007-aspnet-core-identity-three-role-model.md) | ASP.NET Core Identity with a three-role model (Admin / Super User / User) | Accepted (partially implemented; see Consequences) |
| [0008](0008-tiered-large-upload-queue.md) | Tiered large-upload processing through a database-backed job queue (`ScanUploadJob`) | Accepted |
| [0009](0009-evaluation-docker-flavors-sqlite-postgres.md) | Self-contained SQLite and PostgreSQL Docker flavors for evaluation | Accepted |
| [0010](0010-mcp-server-for-qa-data-exploration.md) | Read-only MCP server for QA data exploration, kept separate from the Integration Hub API | Accepted |

## Status values used

- **Proposed**: under discussion, not yet implemented.
- **Accepted**: implemented and in active use.
- **Accepted (contested)**: implemented, but code review surfaced consequences serious enough that the decision should be revisited before v1.0.
- **Superseded by NNNN**: replaced by a later ADR (link included).
- **Deprecated**: no longer applicable; kept for history.

## Template for new ADRs

```markdown
# NNNN. Title

Status: Proposed | Accepted | Superseded by NNNN | Deprecated
Date: YYYY-MM-DD

## Context
What forces / constraints / problem led to this decision?

## Decision
What did we decide to do?

## Consequences
What becomes easier or harder as a result? Include known trade-offs and follow-ups honestly.

## Alternatives considered
What else was possible, and why wasn't it chosen?
```
