# 0002. Single shared SQL Server database across all components

Status: Accepted
Date: 2026-09-01 (reconstructed from the v0.6 codebase)

## Context
Scans can originate from three different entry points (manual web upload, FTP-polled Worker Service, HTTP API). Reviewers need to see and act on all of them from one place, the web UI, regardless of how they arrived.

## Decision
All components connect to the same SQL Server database (default name `AuditTrailReview`) through the same `AuditTrailReview.Database.ApplicationDbContext`, configured via each component's own `ConnectionStrings:DefaultConnection`. There is no per-component database, no message queue, and no synchronization step between components: a row written by any of them is immediately visible to all of them because they're reading and writing the same tables.

**Data retention and backup policy, confirmed 2026-09-02:** both are explicit customer/deployment decisions, not something the application defines or enforces. Backup/restore stays a manual SQL Server DBA procedure (`BACKUP DATABASE` / `RESTORE DATABASE`, see `Technical Manual.md` §5) -- scheduling, frequency, and retention of the backups themselves are the customer's own operational choice, made at the infrastructure level (SQL Server Agent job, managed-SQL backup policy, etc.), not a feature the product builds or prescribes. Scan/audit data retention (how long records are kept before archival or deletion) is likewise left to the customer/deployment: the application persists all scan data indefinitely today, with no built-in archival or deletion job, and that is by design rather than an oversight -- retention periods are frequently dictated by a customer's own regulatory environment, which varies by industry and jurisdiction, so a single policy baked into the product would be wrong for some deployments. This resolves `Path to v1.0.md` item 22 (URS DI-05/DI-06) as a decision rather than a code change.

## Consequences

**Positive:**
- Zero-latency consistency: a scan ingested via FTP at 2am is visible on the Dashboard the moment a reviewer opens it the next morning, with no sync/replication step to reason about.
- Simple to reason about and operate for a single-instance deployment: one connection string to configure per component, one database to back up.
- The internal audit-trail log, user activity log, rule configuration, and engine settings are all naturally singular and consistent across every ingestion path, since there's only one copy of each.

**Negative / known gaps:**
- **No database-level tenant or environment isolation.** All components in an environment must point at exactly the same database, and a misconfigured connection string in one component (e.g. the Worker Service pointed at a different environment's database than the WebApp) would silently ingest into the wrong place with no cross-check.
- **Shared schema evolution risk**, because every component references the same `ApplicationDbContext`: a schema change has to be coordinated across WebApp, API, and Worker Service deployments simultaneously, and there's no API-versioning or schema-versioning boundary between them.
- **Rule/engine-settings changes apply globally and immediately** to every ingestion path with no environment- or system-specific override; see the Rule-Based Review Engine configuration described in `3 Functional & Configuration Specification.md` FR-015/FR-016.
- No connection pooling or resiliency policy (retry/circuit breaker) is layered on top of the raw EF Core SQL Server provider in any component.

## Alternatives considered
- **A message broker between ingestion and processing** (e.g. each front door publishes a "file received" event, a single consumer processes it). Would decouple the front doors from direct database writes and allow independent scaling of ingestion vs. processing, at the cost of operational complexity (broker to run, at-least-once delivery / idempotency to design), and idempotency is notably still an open problem even without a broker; see the Worker Service's duplicate-ingestion-on-restart issue in ADR-0005.
- **A dedicated read API for the web UI**, separate from the write paths. Rejected as unnecessary for the current scale and would add a network hop with no clear benefit while everything is co-located.
