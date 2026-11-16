# Path to v1.0

| Field | Value |
|---|---|
| Product | GxPAuditTrailReview |
| Baseline | v0.9 (in development), `master` @ `9b9e15f` |
| Prepared | 2026-11-05 |
| Updated | 2026-11-06: item 1 partly resolved (re-authentication), item 12 corrected (TC-UI-005 to 008 are implemented) |
| Status | Draft, for prioritisation |
| Companions | [Path to v1.0 - UX and UI.md](Path%20to%20v1.0%20-%20UX%20and%20UI.md), [Path to v1.0 - Cybersecurity.md](Path%20to%20v1.0%20-%20Cybersecurity.md) |

## Purpose

This is the list of functionality still to build before v1.0, the first release intended for production GxP use. The previous `Path to v1.0.md` (under `Requirements/Product/`, removed in commit `19ce011`) closed all of its Tier 1 and Tier 2 items. This list starts again from the current code and the open points in the Validation Package, ADRs, PRD and v0.9 release notes.

Items marked **(verified)** were checked against the source code on the date above. The other items come from the documents and still need a code check before work starts.

## Priorities

- **P1 - Required for v1.0:** a 21 CFR Part 11 / EU Annex 11 expectation that isn't met, a security exposure, or a validation deliverable that is missing.
- **P2 - Should be in v1.0:** an operational or functional gap a production customer will hit, or a URS requirement with no implementation.
- **P3 - Can follow v1.0:** an improvement. Shipping without it is acceptable if it is listed as a known limitation in the UAT document, section 5.

---

## Summary

| # | Item | Area | Priority |
|---|---|---|---|
| 1 | Re-authentication and signature meaning on sign-off (**partly resolved**) | Compliance | P1 |
| 2 | Tamper evidence for signatures and the audit trail | Compliance | P1 |
| 3 | Secure FTP transport (FTPS / SFTP) | Security | P1 |
| 4 | Role policy for review, sign and System CRUD | Compliance | P1 |
| 5 | Complete and execute the Validation Package | Validation | P1 |
| 6 | Close the remaining gaps from the self-registration fix | Security | P1 |
| 7 | Ingestion and upload-job monitoring page | Operations | P2 |
| 8 | Health-check endpoints for WebApp, API and Worker Service | Operations | P2 |
| 9 | Automated database schema upgrades | Deployment | P2 |
| 10 | API key lifecycle: expiry, scopes, last-used | Security | P2 |
| 11 | Streaming parsing for large files | Performance | P2 |
| 12 | Test automation gaps | Quality | P2 |
| 13 | Release deliverables: SBOM, release notes, docs alignment | Release | P2 |
| 14 | Admin-editable vendor field mappings | Functionality | P3 |
| 15 | Admin-configurable AI prompt | Functionality | P3 |
| 16 | Worker Service parallelism and job priority | Performance | P3 |
| 17 | MCP server extensions | Functionality | P3 |
| 18 | Housekeeping | Code | P3 |

---

## P1 - Required for v1.0

~~ ### 1. Re-authentication and signature meaning on sign-off (verified)

> **Partly resolved 2026-11-06.** Re-authentication is done. **Close review & sign** and **Reopen review** on `Scan/Details` now open a signing pop-up with the login prefilled (read-only, and checked on the server against the signed-in account) and a password field checked with `UserManager.CheckPasswordAsync`. A wrong password is logged to the login log (`SignatureFailed`) and counts toward lockout through `AccessFailedAsync`; at the threshold the account is deactivated as on Login, the session is signed out and the user lands on the Lockout page. Covered by new tests in TC-783. **Still to do:** the "meaning of signature" choice and the optional comment (the reasons are still the fixed "Reviewed & Signed" / "Reopen review"), and showing the meaning on the page and in the export.

`Scan/Details.cshtml.cs` `OnPostSignAndCloseAsync` creates the `ScanSigned` row using only the existing login session. The reason is always the fixed text `"Reviewed & Signed"`. 21 CFR 11.200(a) requires a signature to use two identification components (user ID and password) at signing, and 11.50 requires the meaning of the signature (for example review, approval, responsibility) to be recorded.

To build:
- A signing dialog that asks for the password again (and optionally the user name) and checks it with `UserManager.CheckPasswordAsync`. A wrong password is logged and counts toward lockout.
- A required "meaning of signature" choice (Reviewed / Approved / ...), plus an optional comment, stored in `ScanSigned.Reason`.
- The same dialog for Reopen, so the reopen signature meets the same standard.
- The signature meaning shown on Scan Details and in the Excel export (it already shows user, role, date and reason).

Refs: URS-013, NFR-008, Risk Assessment row 9.

### 2. Tamper evidence for signatures and the audit trail

Signature rows and internal audit-trail rows are ordinary database rows. Anyone with database write access can change them without leaving a trace (GAP-009, UAT section 5 item 5).

To build:
- When a scan is signed, store a hash of what was signed: the scan's results and review decisions plus the signer and timestamp. Show a "content changed since signature" warning if the hash no longer matches.
- Hash-chain the `AuditTrail` rows (each row stores the previous row's hash) and add a "Verify integrity" action for admins on `Log -> Audit trail log`.
- Optionally, deny `UPDATE` and `DELETE` on `ScanSigned` and the audit log tables to the application's SQL login (an add-on to the schema scripts).

### 3. Secure FTP transport (FTPS / SFTP) (verified)

`FtpFileCheckerService` uses FluentFTP with no encryption setting, so credentials and audit-trail files travel in clear text (GAP-003, UAT section 5 item 2).

To build:
- ~~`Ftp:EncryptionMode` (None / Explicit / Implicit) with certificate validation for FTPS.~~ **Done, 2026-11-09:** FTP settings moved into the database and the Admin Panel (Settings > FTP) with a master switch (off by default), None / Explicit / Implicit FTPS, certificate validation and optional thumbprint pinning.
- SFTP support (for example through SSH.NET) behind the same `IFtp...` abstraction, since many customer servers only offer SFTP.
- Installer and Installation Manual updates.

### 4. Role policy for review, sign and System CRUD

v0.9 restricted Reopen, Scan delete and System delete to Admin / Super User. Review, Sign & Close, and System create/edit remain open to every signed-in role (GAP-011, UAT-10 step 3). That is currently a product choice, but it has not been formally decided.

To decide, then build:
- Which roles may review findings and which may sign. Consider a dedicated "Reviewer/QA" permission.
- Whether the person who reviewed the findings may also sign (segregation of duties), and whether the uploader may sign their own scan.
- Enforcement on the server side in the page handlers, and an updated permissions matrix in the User Manual and ADR-0007.

### 5. Complete and execute the Validation Package

- **`Gap Analysis.md` is missing.** The FRS, Risk Assessment and UAT all link to it (GAP-002 ... GAP-024), but the file isn't in `Validation Package/`. Restore it or recreate it.
- **Traceability Matrix:** most URS, FR and NFR rows show 🔴 GAP (no IT/CT/FT test case). Write the missing test cases in the IQ/OQ protocol, or record why each requirement is covered elsewhere (unit tests, UAT).
- **Performance Qualification** (URS-025, NFR-015/016/044) has not been written. Define representative volumes and targets, then run it.
- **Execute** IQ/OQ and UAT against the v1.0 build, and record deviations.
- **UAT section 5:** the business owner has to accept each remaining known limitation.
- **Validation Report:** fill in sections 1, 2, 4, 7 and 9. Section 8 already states the system is validated, which should only be said after execution. Fix the typo "Pleas" in section 3.

### 6. Close the remaining gaps from the self-registration fix

Commit `85151fd` switched off `/Identity/Account/Register` and the other unused Identity pages (see `Security/DisabledIdentityPages.cs` and TC-791). Before that fix, anyone could create an account and sign in. Three things are still needed before v1.0:

- **Find accounts created through Register.** Existing databases may already hold accounts created that way. Before upgrading, check every installation for users with no row in `AspNetUserRoles`, and confirm each one with the customer. Deactivate or delete the ones nobody can account for, and record the outcome. A ready-made SQL query in the Installation Manual (or a "users without a role" filter on `AdminPanel → Users`) would make this repeatable.
- **Audit Profile name changes.** Users can change their own first and last name on `Identity/Account/Manage` (Profile), and the change isn't written to the audit trail. Either log it like the admin's Edit User page does (old value, new value, who, when), or make the fields read-only and leave name changes to an Admin.
- **Add an end-to-end UI test.** No Playwright test yet checks that `/Identity/Account/Register` returns 404. Add one to `AuditTrailReview.UIPlaywrightTest/SmokeTests.cs`, with a matching TC-UI document, so the hole can't reopen without the test suite noticing (TC-791's unit tests check the conventions, not the HTTP pipeline).

---

## P2 - Should be in v1.0

### 7. Ingestion and upload-job monitoring page (verified)

Failed `ScanUploadJob` rows and FTP files moved to `FailedFolder` are only visible in the database, the logs, and the uploader's notification bell. No page lists them (URS-028, GAP-021, NFR-032).

To build: an Admin page that lists upload jobs (status, tier, source, retries, `ErrorMessage`, timestamps) and FTP ingestion records, with filtering, an Excel export, and a "requeue" action for failed `Tier.Worker` jobs.

### 8. Health-check endpoints (verified)

Only `AuditTrailReview.McpServer` exposes `/health`. Add `MapHealthChecks` with a database check to the WebApp and the Integration Hub API, and a heartbeat (last poll time, queue depth) for the Worker Service. Use them in the Docker compose files and the deploy pipeline.

### 9. Automated database schema upgrades

ADR-0004 is still "Accepted (contested)". Installing means running a snapshot plus incremental SQL scripts by hand, in version order (not alphabetical order). Make a decision for v1.0: either EF Core migrations, or a script runner (for example DbUp) with a `SchemaVersion` table, so that the WebApp or the deploy pipeline applies pending scripts and refuses to start on a schema mismatch. Either way, produce a v1.0 baseline snapshot.

### 10. API key lifecycle

API and MCP keys are now stored encrypted in the database and can be revoked. Still missing:
- an optional expiry date, plus a warning before a key expires
- a "last used" timestamp and IP for each key
- scope (for example restricting a key to particular systems)

OAuth/JWT stays a known limitation (GAP-002 / GAP-010) unless a customer requires it.

### 11. Streaming parsing for large files

Files up to 750 MiB are fully read into memory before parsing, so peak memory grows with the row count (ADR-0008, Technical Manual "Large-file processing"). Parse CSV, XML and JSON as a stream and evaluate the rules in batches, so the Worker Service's memory use stays bounded.

### 12. Test automation gaps

- Playwright smoke tests: all of TC-UI-001 to 008 are implemented. TC-UI-005 (menu links) is in `MenuLinkCrawlerTests.cs`; TC-UI-001 to 004 and 006 to 008 (small CSV upload, 404 page, Help pages) are in `SmokeTests.cs` since `48d572a`. `Matrix-TestCases-Tests.md` still lists TC-UI-006 to 008 as "(planned)"; update it to name the test methods. The TC-UI-007 test should also check the friendly "Page not found" page added in `aeecf35` (UX/UI list item 5).
- Provision a test database in CI so the UI tests (Playwright or Selenium) can run in the pipeline.
- The PostgreSQL live blob stream and `ExecuteUpdateAsync` paths are only covered at the SQL-text level (ADR-0009).
- The `PasswordSignInAsync` lockout-escalation flow has no test (TC-805 "Not covered").
- TC-401 (FTP polling) is manual only.

### 13. Release deliverables and documentation alignment

- ~~SBOM for v1.0. v0.9's SBOM was never produced.~~ **Done:** `ReleaseNotes/SBOM/SBOM v0.9.md` (regenerate on the release commit).
- ~~Release notes for v1.0.~~ **Done:** release notes are folded into `ReleaseNotes/RN v0.9.md` (set the release date when the release is cut).
- ~~Several documents described gaps that have since been fixed in the code (NFR-043).~~ **Done:** PRD section 12 and AI wording, UAT section 5 items 4 and 9, FRS section 12 and the Traceability Matrix (FR-041/042 withdrawn), ADR-0010 and the Technical Manual (health endpoint, MCP in `docker/docker-compose.postgres.yml`), and the Installation Manual, Technical Manual and ADR-0004 (SQL scripts up to `ATR 0.40`, incremental-script approach) are brought up to date.

---

## P3 - Can follow v1.0

### 14. Admin-editable vendor field mappings

The header-keyword mapping in `AuditTrailMapper` is maintained in code, so supporting a new vendor's column names needs a code change and a redeploy (GAP-005, URS-008, NFR-026). Add a Config Panel screen for keyword mappings per system type, with audit trail and a preview against a sample file.

### 15. Admin-configurable AI prompt

The model can now be chosen (`gpt-4o-mini` / `gpt-5-mini`), but the prompt text is still fixed (PRD section 13 open question). If this is built, the prompt must be versioned and audit-logged, and the prompt version used must be stored with each `ScanResultAI` row so an analysis can be reproduced.

### 16. Worker Service parallelism and job priority

`Tier.Worker` jobs are processed one at a time, oldest first, so a 700 MB file holds up every job behind it for minutes. Options: a configurable degree of parallelism, a separate lane for small files, or several Worker instances that claim jobs safely.

### 17. MCP server extensions

Today the MCP server has two read-only tools (`list_systems`, `list_scans`), supports SQL Server only, and uses one static key with no per-user identity (ADR-0010). Candidates: `get_scan_findings`, `get_scan_summary`, audit-trail queries, a `Database:Provider` switch, and per-user keys so MCP access can be attributed to a person.

### 18. Housekeeping (done)

- ~~Delete `WeatherForecastController.cs` and `WeatherForecast.cs` (template leftovers in `AuditTrailReview.IntegrationHub.API`).~~ Done.
- ~~Remove the unused `ReasonMinAlphabeticChars` column (retired rule A5, see `Audit-Trail-Rule-Engine.md`).~~ Already done by `ATR 0.20 20261026 - EngineSettings Drop ReasonMinAlphabeticChars.sql`.

---

## Explicitly out of scope for v1.0 (decided)

These items come up repeatedly, but they have already been decided against. They are not gaps:

- Parsing PDF or Excel audit trails (URS-022; CSV/XML/JSON only).
- Creating CAPAs or deviations automatically, and QMS workflow orchestration (PRD section 6.2).
- MFA (removed from the requirements scope, ADR-0007).
- Backup scheduling and data retention or archival inside the application. These are the customer's infrastructure responsibility (ADR-0002, decided 2026-09-02). The PostgreSQL Docker flavor's nightly `pg_dump` is a convenience for evaluation only.
- Swagger/OpenAPI in production (Development only, ADR-0005).
