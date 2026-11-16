# Product Requirements Document (PRD) v0.9

## Document Control
- **Product:** GxPAuditTrailReview
- **Version:** Draft based on codebase v0.9
- **Status:** Working draft
- **Source of truth:** Current solution code and documents in `AuditTrailReview.Docs`

---
## Current solution (codebase snapshot)

Summary of projects present in the solution (all projects target .NET 10 / net10.0):

- **AuditTrailReview.WebApp**: Razor Pages web application (UI, ASP.NET Core Identity, EF Core), containerized via its own `Dockerfile`.
- **AuditTrailReview.IntegrationHub.API**: ASP.NET Core Web API exposing `POST /api/ScanProcessing/scan`, authenticated with an `X-Api-Key` header checked against configured keys; containerized.
- **AuditTrailReview.IntegrationHub.WorkerService**: hosts the Integration Hub Worker Service (`Worker.cs`, project file `AuditTrailReview.IntegrationHub.WorkerService.csproj`) plus the FTP polling logic (`FTP/FtpFileCheckerService.cs`) and the `ScanUploadJob` queue poller; containerized. The earlier duplicate, largely-empty folder has been removed; only this one project exists now.
- **AuditTrailReview.Database**: EF Core `ApplicationDbContext` and shared model access. Schema changes are managed via versioned raw SQL scripts in `Database schema/` (periodic full snapshots, e.g. `ATR 0.8`, plus incremental scripts layered on top), not EF Core code-first migrations.
- **AuditTrailReview.Engine**: core business logic: file parsing, header-keyword mapping to a common model, the 19-rule review engine, OpenAI-based AI analysis, and shared services (email, user activity logging, internal audit trail logging).
- **AuditTrailReview.Model**: shared domain model types (scan, system, rule, engine settings, review status, etc.).
- **AuditTrailReview.Console**: a single cross-platform console client (the earlier duplicate `AxteonAuditTrailReview.ConsoleApp` project has been removed) that posts a local file to the Integration Hub API, configured via a per-instance `appsettings.json` next to the exe, with an optional `appsettings.Development.json` override (as of `Path to v1.0.md` item 30, file-only as of its Thirty-fourth follow-up), meant for standalone deployment to many different machines without requiring OS-level environment variable setup on each one. It does not currently ship a `Dockerfile`.
- **AuditTrailReview.UnitTest**: MSTest unit tests, currently covering `AuditTrailReview.Engine` components (file mapping, date/time checks, login-name heuristics, scan processing, rule execution, unique-identifier detection, activity/audit logging services). No automated tests currently cover the WebApp Razor Page handlers or the Integration Hub API controller.
- **AuditTrailReview.UISeleniumTest**: UI tests using Selenium WebDriver.
- **AuditTrailReview.Docs**: documentation project (this docs set).

Refer to `AuditTrailReview.Docs/Tech/TechStack.md` for more detailed tech stack and project responsibilities.


## 1. Product Summary
GxPAuditTrailReview is a web-based audit trail review platform for GxP-relevant systems. The product enables users to upload audit trail files, standardize incoming records, apply configurable rule-based checks, review findings, and close scans with sign-off. It also includes an Integration Hub (a Worker Service that polls FTP, and an authenticated HTTP API) plus a console client, so audit trail files can be ingested automatically or scripted, in addition to manual upload; all paths write into the same shared database.

The current implementation is centered on:
- A Razor Pages web application for operational use
- A rule-based review engine (19 rules across 5 categories) with configurable thresholds and per-rule enablement
- An AI analysis step (OpenAI by default; provider and model configurable) that runs automatically after every scan
- Database-backed persistence for systems, scans, findings, signatures, logs, and engine settings
- A Worker Service (FTP polling) and an authenticated HTTP API for background/external ingestion, both sharing the same processing pipeline
- A console client for scripted/CLI submission to the API

---

## 2. Problem Statement
GxP-regulated organizations receive audit trails from multiple systems and vendors in inconsistent formats. Manual review is time-consuming, difficult to standardize, and hard to trace. Teams need a repeatable way to:
- ingest audit trail data from different sources,
- normalize it into a common structure,
- detect suspicious or non-compliant events,
- document review outcomes, and
- keep an auditable record of review and sign-off.

---

## 3. Product Vision
Provide a practical and traceable audit trail review platform that helps quality, validation, and system owners review audit trails consistently across vendors and file formats.

---

## 4. Goals
### 4.1 Business Goals
- Reduce manual effort required to review audit trail files.
- Standardize review across systems and vendors.
- Improve traceability of findings, review actions, and sign-off.
- Support scalable ingestion through manual upload, Integration Hub FTP polling, the Integration Hub API, and the console client.

### 4.2 Product Goals
- Support upload and processing of CSV, XML, and JSON audit trail files.
- Associate scans with systems and system types.
- Execute configurable rule-based checks against normalized records.
- Allow reviewers to assess findings and close scans, with review-completeness enforced in the UI and re-checked by the server-side close-and-sign handler.
- Provide dashboards, scan history, logs, and export capability.

### 4.3 Non-Goals for the Current Baseline
- Direct parsing of PDF, Excel, or live API payloads in the current release.
- Making the AI prompt's wording admin-configurable: the provider, endpoint and model are admin-configurable on the AI Analysis tab (the model restricted to an allow-list so the JSON output shape cannot break), but the fixed GxP/21 CFR Part 11 instruction text itself is not (see §7.5 and §12).
- Automated vendor-specific adapters for every system named in reference documents.

---

## 5. Target Users
The product model defines three Identity roles (**Admin**, **Super User**, and **User**), but role-based access is only actually enforced for user/role administration today (see §12). Until that gap is closed, treat the descriptions below as the intended, not fully enforced, division of responsibility:
- **Admin:** Manages users, roles, and (by intent) privileged configuration. In code, only user and role administration are actually restricted to this role.
- **Super user:** Intended to manage rule configuration, engine settings, and selected operational tasks. In code, rule configuration, engine settings, and system type management are reachable by any signed-in user, not gated to this role.
- **User:** Uploads files, reviews findings, and works with assigned scans.
- **Quality / Compliance reviewer:** Reviews findings and signs off completed scans.
- **System owner / process owner:** Associates scans with business systems and reviews outcomes.

---

## 6. Scope
### 6.1 In Scope
- Authentication and (partially enforced) role-based access
- Dashboard with scan and review metrics
- System registry with system type classification
- Manual audit trail upload from the UI, including a separate preview-only page
- Audit trail parsing for CSV, XML, and JSON
- Mapping to a common audit trail model via header-keyword heuristics
- Rule-based scan result generation (19 rules, 5 categories)
- Automatic AI-assisted analysis on every scan, whatever the ingestion path
- Findings review workflow
- Scan close and sign workflow
- Reopen workflow (currently available to any authenticated user, not role-gated; see §12)
- Scan export (Excel) and file preview
- User login log and internal audit trail log (currently scoped to System record changes only; see §12)
- User credential delivery by email (admin-triggered password reset link)
- Integration Hub FTP-based ingestion (Worker Service)
- Integration Hub HTTP API (API-key authenticated) for external/scripted submission
- Console client for CLI submission to the Integration Hub API

### 6.2 Out of Scope
- Workflow orchestration across external QMS systems
- Automated CAPA or deviation creation
- Native OCR/document intelligence for unstructured documents
- Full cloud connector coverage beyond the current FTP-focused Worker Service and the generic HTTP API
- Formal regulatory validation package generation

---

## 7. Core Capabilities
1. **System management**
   - Create and maintain systems.
   - Assign a system type.
   - Run scans at system level.
   - System create/edit/delete actions are recorded in the internal audit trail log.

2. **Audit trail ingestion**
   - Manual upload through the web UI (with an independent preview-only page).
   - Automated ingestion through the Integration Hub Worker Service (FTP polling).
   - Scripted/external ingestion through the Integration Hub HTTP API (API-key authenticated) and the console client.
   - Support for CSV, XML, and JSON input files; the file extension is also checked server-side (`AuditTrailFileParser.IsSupportedExtension`) on manual upload, preview and the API, in addition to the client-side `accept` hint.
   - WebApp upload size tiers (manual upload through the UI):
     - **Small (under 10 MB):** processed synchronously, inline in the request.
     - **Medium (10 MB to 50 MB):** processed asynchronously in the WebApp process (`Tier.Immediate`); lost and marked `Failed` on a WebApp restart, since the bytes only live in memory.
     - **Large (over 50 MB, up to 750 MB / 786,432,000 bytes, the hard upload ceiling):** processed asynchronously by the Integration Hub Worker Service (`Tier.Worker`), one file at a time.
   - Every API upload (any size) is validated and stored as a `Tier.Worker` row instead, and answered with `202` + a job ID for polling; the API itself never processes a file.

3. **Normalization and rule execution**
   - Convert incoming files into a common internal structure using header-keyword mapping.
   - Run the 19 configurable rule checks (categories A–E).
   - Persist scan results and findings, each tagged with its triggering rule code.

4. **Review and sign-off**
   - Review findings and capture review outcome (False Positive / Investigation Required / Deviation), comment, reviewer, and timestamp.
   - The UI discourages closing a scan while findings remain unreviewed (disabled button); this is not currently re-checked by the server.
   - Close and sign completed scans, recording a signature entry with user, role(s), timestamp, and reason.
   - Reopen scans, recording a corresponding signature entry; restricted to the Admin and Super User roles.

5. **AI-assisted analysis**
   - After any scan's rule-based results are saved (manual upload, Worker Service, or API-ingested alike), the system automatically calls the configured AI provider and model (default OpenAI `gpt-4o-mini`; provider, endpoint and model are set on the AI Analysis tab) in the background with a GxP/21 CFR Part 11-focused prompt, storing the returned insights/recommendations against the scan.
   - What's sent is tiered by scan size, so a very large scan (up to ~5,000,000 rows) can't make the AI step unbounded in latency or cost: a scan at or under an admin-configurable row threshold (`AiSettings.FullScanRowThreshold`, default 300) still sends its entire result set, so the AI independently analyzes the whole audit trail for the common case; a larger scan instead sends an evenly-sampled set of rule-engine findings, up to a second admin-configurable cap (`AiSettings.MaxFindingsPerAnalysis`, default 200), plus a statistical summary covering every row of the scan (counts by user/operation/object type, hour-of-day and day-of-week distribution, missing-reason rate), so the AI can still reason about scan-wide patterns it doesn't see row-by-row.
   - Displayed in a dedicated AI Analysis tab and included in the Excel export.
   - Can be turned off globally (`EngineSettings.AiAnalysisEnabled`); this toggle and the two payload-tiering settings above are all configured together on the AI Analysis tab of `AdminPanel/Settings`.
   - Requires a configured API key (stored encrypted in the database); if absent, the AI step fails silently in the background and the scan itself is unaffected.

6. **Administration and configuration**
   - Configure rule enablement and engine thresholds (working hours and time zone, weekend handling, username length, shared/system-account patterns, mass-change burst threshold and window).
   - Manage users (including role assignment and credential-email delivery) and roles; Admin-only.
   - View user login and internal audit trail logs, with Excel export.

---

## 8. Functional Summary
The detailed requirements are defined in `3 Functional & Configuration Specification.md`. At a summary level, the product shall:
- authenticate users and enforce role-based authorization (Admin-only administration; Admin/Super User for configuration, deletion, reopen and reassignment),
- manage systems and system types,
- upload, parse, and scan audit trail files (manually, via FTP polling, or via the API/console client),
- generate findings from the 19 configurable rules,
- automatically run AI-assisted analysis on every scan, whatever the ingestion path,
- support review, sign-off, reopen, export, preview, and assignment workflows,
- record user login activity and internal audit trail activity (systems, scans, reviews, sign/reopen, configuration and user/role administration),
- process external files via the Integration Hub Worker Service and API.

---

## 9. Non-Functional Summary
The detailed requirements are defined in `3 Functional & Configuration Specification.md`. At a summary level, the product shall be:
- secure and role-aware (role enforcement is server-side; see that document's NFR-002),
- traceable and auditable,
- reliable during scan processing and background ingestion (already-ingested FTP files are tracked in `dbo.IngestedFile`, so a Worker Service restart does not re-ingest them; see NFR-011),
- maintainable through clear project separation (parsing is shared in `AuditTrailFileParser`; the former duplicate console-client project has been removed),
- observable through application logs,
- extensible for additional vendors and connectors.

---

## 10. Success Metrics
### 10.1 Operational Metrics
- Users can upload a supported audit trail file and receive persisted results without manual database intervention.
- Users can review findings and close a scan once review is marked complete in the UI.
- The Integration Hub can detect eligible FTP files and create scans in the shared database, and external systems can submit files via the API using a valid API key.

### 10.2 Product Adoption Metrics
- Number of systems registered
- Number of scans processed per period
- Number of scans ingested through Integration Hub (FTP or API) versus manual upload
- Average time from scan creation to sign-off
- Percentage of findings reviewed before closure

### 10.3 Quality Metrics
- Parsing success rate for supported file formats
- Failed scan rate, and AI-analysis failure rate across all ingestion paths tracked separately (an AI failure never fails the underlying scan, see NFR-014)
- Reopened scan rate
- Rule configuration coverage and usage
- Duplicate-scan rate from Worker Service restarts (see Risks and Gaps)

---

## 11. Constraints and Assumptions
- Current supported input formats are CSV, XML, and JSON.
- Current automated ingestion is implemented via FTP polling in the Integration Hub Worker Service, plus an API-key-authenticated HTTP API for direct/scripted submission.
- Scan rule execution depends on database-stored rule enablement and engine settings, read fresh at the start of each scan.
- The web application is implemented using Razor Pages.
- The platform currently uses a shared database for the web app, Worker Service, and API.
- Database schema is currently managed via versioned raw SQL scripts rather than EF Core migrations.
- AI analysis runs as a non-blocking background step after every scan's rule-based results are saved, regardless of ingestion path; it can be turned off globally and its failure never blocks or fails the underlying scan.

---

## 12. Risks and Gaps
Earlier drafts listed several gaps that have since been fixed in code and are no longer risks: missing role enforcement on configuration, reopen and system pages; close-and-sign not checked on the server (it now is); the in-memory FTP "already processed" list (now `dbo.IngestedFile`); a secret tracked in source control (secrets are now encrypted in the database or supplied through gitignored configuration); the audit log being limited to System records (it now also covers scans, reviews, sign/reopen, configuration and user/role administration); and file-parsing code duplicated three times (now shared in `AuditTrailFileParser`). The remaining items are:
- **AI prompt wording is not admin-configurable:** the provider, endpoint and model are configurable on the AI Analysis tab, but the fixed instruction text is not.
- Vendor audit trail structures vary significantly, increasing mapping complexity.
- Poorly structured input files may reduce rule accuracy.
- Large files may create memory pressure, since files are fully read into memory before parsing (no streaming).
- Additional connector types shown in concept diagrams are not equivalent to implemented production support (only FTP and a generic HTTP API exist today).

---

## 13. Open Questions
- What are the approved processing-time limits for large files? (File size limits are now defined; see §7, "Audit trail ingestion".)
- Should reviewing findings and signing scans be restricted to different roles in regulated use?
- Should the AI prompt's wording be made admin-configurable, beyond the existing on/off toggle, provider/model selection and payload-sizing settings?
- What is the expected retention period for scan records, signatures, and logs?
- Which vendor-specific mappings should be prioritized next?
- Should database schema management move to EF Core migrations?

---

