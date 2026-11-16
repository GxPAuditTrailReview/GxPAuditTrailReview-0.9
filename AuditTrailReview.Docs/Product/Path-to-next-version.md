# Path to next version

| Field | Value |
|---|---|
| Product | GxPAuditTrailReview |
| Baseline | `master` @ `3334c26` |
| Generated | 2026-11-12 by the `create-path-to-new-version` skill. Regenerate it; don't edit by hand. |
| Sources | [Path to v1.0.md](Path%20to%20v1.0.md), [Path to v1.0 - Cybersecurity.md](Path%20to%20v1.0%20-%20Cybersecurity.md), [Path to v1.0 - UX and UI.md](Path%20to%20v1.0%20-%20UX%20and%20UI.md) |
| Target version | v1.0 |

## Purpose

The three source documents say what is missing and how important each item is. This document says **when to do it**.

- **NOW** is work for the current version that stays useful in every later version. It passes one of five tests, applied in order: (1) an exposure or an integrity problem today, (2) it gets costlier once more data or customers exist, (3) other items depend on it, (4) it is a decision that blocks other work, (5) it is cheap and permanent.
- **LATER** is work that can wait without a cost: purely additive, would be redone anyway, blocked by a NOW item, or needed only by some installations.

Item IDs point back to the sources: **F-n** is `Path to v1.0.md`, **S-n** is the Cybersecurity list, **U-n** is the UX and UI list, and *n* is the item number there. The sources were written on 2026-11-05/06; this file checks them against the code and the commits up to the baseline. Items marked *(re-checked)* were confirmed in the code on 2026-11-12; the other facts are taken from the sources.

## Summary

| | Count |
|---|---|
| NOW (grouped rows) | 20 |
| LATER (grouped rows) | 20 |
| NOW rows that need a decision first (rows 5, 6, 7, 8, 10, 11) | 6 |
| NOW rows that need a schema script (rows 1, 2, 6, 10, 11; row 4 maybe) | 5 |
| Items closed (9 in full, 2 in part), not triaged | 11 |

---

## NOW - do in the current version

In working order. A row can cover several source items when they touch the same code, so one pass does them.

| Order | ID | Item | Source priority | Why now | Needs | Validation impact |
|---|---|---|---|---|---|---|
| 1 | S-3 | Seeded admin password is public: remove it from tracked `appsettings.json`, add a "must change password at next sign-in" flag, warn while the initial password is in use *(re-checked: still in `appsettings.json`)* | P1 | Test 1: published credentials | schema script (flag) | Tests for the flag and the warning; Installation Manual step |
| 2 | S-6 | Master encryption key: fail fast at startup on a missing/placeholder/short key, key ID stored with each ciphertext so the key can be rotated, secrets inventory | P1 | Tests 1 and 2: every stored key is encrypted with it, and rotation gets harder with more ciphertext | schema script (key ID) | Tests for startup check and rotation; Installation Manual inventory |
| 3 | S-5 | Docker: do not publish PostgreSQL 5432, bind app ports to `127.0.0.1`, refuse to start with the placeholder `POSTGRES_PASSWORD` *(re-checked: `"5432:5432"` still published)* | P1 | Test 1: database reachable on every interface | none | Update `CICD/Run docker compose on VPS.md`; manual check only |
| 4 | S-10 | Persist and protect Data Protection keys (nothing calls `AddDataProtection`, *re-checked*) | P2 | Test 3: item 2 protects secrets, and sessions and forms survive restarts only once the key ring is stable | schema script if stored in the database | Test that a key ring survives restart; User Manual note on sign-out after redeploy goes away |
| 5 | F-4 | Role policy for review, sign and System CRUD, including segregation of duties, then server-side enforcement | P1 | Tests 4 and 3: the sign-off and UI items below depend on the answer | **decision**, then docs | New role tests; permissions matrix in User Manual and ADR-0007; UAT-10 |
| 6 | F-1 + U-2 + F-2 | Sign-off in one pass: signature **meaning** and comment (the re-authentication is done), show it on the page and in the Excel export, a required reason for Reopen, **hash of what was signed**, hash chain on `AuditTrail` and an admin "Verify integrity" action | P1 | Test 2: signatures and audit rows that exist before hashing can't be protected retroactively; all of it changes `ScanSigned` | schema script, decision on the meaning list | Extend TC-783; new TCs for the hash and verify action; FT/UAT steps; Part 11 attestation text in User Manual |
| 7 | S-2 + S-9 | Lockout and login rate limiting: replace permanent deactivation after N failures with an escalating time-based lockout, per-IP rate limit on Login, Forgot password, Reset password | P1 / P2 | Tests 1 and 3: anyone can lock out a colleague; the real client IP this needs is fixed (see Closed) | **decision** with QA | Update TC-805 and the lockout tests (also closes the F-12 "no escalation test" gap); FT step |
| 8 | S-12 (part) | New installations start with AI analysis **off** (`Program.cs` seeds `AiAnalysisEnabled = true`, *re-checked*) | P2 | Test 1: the first scan sends data to a third party before anyone approved it | **decision** (data-protection owner) | Seed-data test; Installation Manual and User Manual wording |
| 9 | F-6 + F-12 (part) | Self-registration follow-ups: SQL query for accounts with no role, audit Profile name changes, Playwright test that `/Identity/Account/Register` returns 404 *(re-checked: no such test)*; rename TC-UI-006 to 008 in `Matrix-TestCases-Tests.md` | P1 / P2 | Tests 1 and 5 | none | New TC-UI doc and matrix rows (`update-docs-test`) |
| 10 | F-9 | Automated schema upgrades: decide EF migrations vs a script runner with a `SchemaVersion` table, and cut a v1.0 baseline snapshot | P2 | Tests 4 and 2: every installed database makes a later switch harder | **decision** (ADR-0004), schema script | New ADR status; Installation Manual; IQ steps |
| 11 | F-10 + S-13 | API and MCP keys in one pass: expiry, last-used time and IP, scope; decide on storing hashes instead of encrypted keys; rate-limit buckets per key or behind a known proxy; TLS as mandatory for MCP | P2 | Test 2: changing key storage later means migrating every issued key | **decision**, schema script | Update TC-807 and the API key TCs; IQOQ steps |
| 12 | U-4 | Show the time zone on every timestamp (abbreviation, UTC in a tooltip, zone in Excel headers) *(re-checked: the tag helper has no zone output)* | P1 | Test 1: ambiguous times in GxP evidence | none | Update the tag-helper tests and export tests; UAT screenshots |
| 13 | U-11 + U-21 | One status-message partial instead of ~12 `TempData` keys (`FtpSuccess` was added after the review), modal for destructive actions, and the markup clean-up on the same pages | P2 / P3 | Test 3: every new page copies the current pattern, so it is cheaper before more pages exist | none | Page-model tests that read the old keys change; TC-UI smoke check |
| 14 | U-8 + U-12 (part) | Upload entry point on the Scans list and Dashboard, correct the Help text; footer version read from the assembly *(re-checked: still hard-coded `v 0.9`)* | P2 | Test 5: Help text that points to a menu item that doesn't exist; screenshots as validation evidence | none | Help page test; User Manual |
| 15 | U-13 + S-8 (part) | Self-host Bootstrap Icons in `wwwroot/lib` (update from 1.8.1), add Subresource Integrity to any remaining CDN link, add the library to the SBOM | P2 | Test 5: icons vanish on an offline GxP network | none | SBOM; CSP test unchanged until the later step |
| 16 | U-9 (quick fixes) | Accessibility quick fixes: contrast on Dashboard cards, `aria-label` on icon-only buttons and the bell, skip link *(re-checked: none in `_Layout.cshtml`)*, `role="alert"` (done as part of row 13) | P2 | Test 5 | none | Optional Playwright accessibility check |
| 17 | F-8 | Health-check endpoints for WebApp and API (database check) and a Worker heartbeat | P2 | Test 3: compose files and the deploy pipeline use them | none | New TCs; Technical Manual |
| 18 | S-11 (part) | CI: fail the build on vulnerable packages, secret scanning *(re-checked: neither is in `pipelines/pipeline-ci-build-unit-test-dev-ops.yml`)* | P2 | Test 5: a few lines, permanent | none | `CICD` documentation |
| 19 | S-15 | Lock in safe parsing: explicit `XmlReaderSettings` / `JsonDocumentOptions`, tests with an XXE payload, a "billion laughs" file, deeply nested JSON and a CSV cell starting with `=` | P2 | Test 5: the code is safe today, and the tests keep it that way | none | New TCs |
| 20 | F-5 (part) | Validation Package: restore or recreate `Gap Analysis.md` (the FRS, Risk Assessment and UAT link to it) and fix the Validation Report typo | P1 | Test 1: a missing validation deliverable | none | `update-docs-validation-package` skill for the matrix |

### Notes on the grouping

- **Rows 5, 6 and 12 touch the same screens and tables** (`ScanSigned`, `AuditTrail`, Scan Details). Do the role decision (row 5) first, then rows 6 and 12 together, so only one schema script is needed for the signature changes.
- **Row 2 before row 4:** the master key and the Data Protection key ring are protected the same way, so choose one secrets approach for both.
- **Row 7 needs QA to decide** which combination of lockout and rate limit the SOPs allow.
- **Schema scripts** go in `AuditTrailReview.Database/Database schema/` as new numbered scripts, with matching changes to the entity and `ApplicationDbContext` (there are no EF migrations; row 10 may change that).

---

## Decisions to take now

| Decision | Blocks | Who decides |
|---|---|---|
| Which roles may review and sign, segregation of duties | Rows 5, 6; UI hiding of actions | QA / business owner |
| List of signature meanings (Reviewed, Approved, ...) | Row 6 | QA |
| Lockout policy: escalation steps and rate limits | Row 7 | QA with security |
| AI analysis default off, and whether to pseudonymise | Row 8, S-12 remainder | Data-protection owner |
| EF migrations vs script runner | Row 10 | Engineering |
| API/MCP keys: hash or encrypted | Row 11 | Engineering with security |

---

## LATER - safe to defer

| ID | Item | Source priority | Why it can wait | Blocked by / revisit when |
|---|---|---|---|---|
| F-3 | SFTP support (FTPS with certificate validation is done) | P1 | The clear-text exposure is closed; SFTP is additive | List as a known limitation; revisit when a customer only offers SFTP |
| F-5 (rest) | Performance Qualification, executing IQ/OQ and UAT, UAT section 5 acceptance, Validation Report sections | P1 | Executes against the release candidate, so it can't start before the NOW work lands | The release candidate |
| S-7 (rest) | ZAP scan, external penetration test, security section in the Risk Assessment (threat model drafted) | P1 | Meaningful only on the release candidate | The release candidate; schedule the vendor now |
| F-7 + U-15 | Upload-jobs page for admins and "My uploads" for users | P2 | Additive; no stored-data change (`ScanUploadJob` already holds everything) | Revisit right after NOW; URS-028 stays a known gap until then |
| F-11 | Streaming parsing for large files | P2 | Performance only; behaviour unchanged | A customer with files near the limit |
| F-12 (rest) | CI test database for UI tests, PostgreSQL live blob stream tests, TC-401 automation | P2 | Test infrastructure, no product impact | The CI test database |
| S-8 (rest) | Remove `'unsafe-inline'` from `script-src` (inline scripts and `onclick=` handlers) | P2 | Touches most pages and would be redone by the page changes in rows 13 and 16 | After rows 13 and 16 |
| S-11 (rest) | Container image scanning, pinned base images, Dependabot, CycloneDX SBOM in the pipeline | P2 | Additive pipeline work; the manual SBOM exists | After row 18 |
| S-12 (rest) | Pseudonymise names before sending, show the endpoint in use, document the fields sent | P2 | Additive once the default is off | After the data-protection decision |
| S-14 | Security event logging and alerting | P2 | Needs the lockout and rate-limit events from row 7 | Row 7 |
| U-6 | Bulk review and review progress on Scan Details (commit `e004c84` mentions a bulk review tab, so the source may be partly out of date; check first) | P2 | Additive; best designed with a QA reviewer | Check what exists, then with U-7 |
| U-7 | Filters on the Scans list and findings tabs | P2 | Additive | Before U-14 |
| U-14 | Dashboard focused on the reviewer's work | P2 | Its links need the filters from U-7 | U-7 |
| U-9 (rest) | Full axe/Lighthouse audit of every page | P2 | Meaningful after the quick fixes and the message pattern | Rows 13 and 16 |
| F-14 | Admin-editable vendor field mappings | P3 | Additive | A customer with a new vendor |
| F-15 | Admin-configurable AI prompt | P3 | Additive; needs prompt versioning | Product decision |
| F-16 | Worker parallelism and job priority | P3 | Additive | A customer with many large uploads |
| F-17 (rest) | MCP: `Database:Provider` switch and remaining tools (query tools and per-user keys are done, see Closed) | P3 | Additive | Check what remains first |
| S-16, S-17, S-18 | Password extras, least-privilege database logins, `SECURITY.md` / `security.txt` and the security checklist | P3 | Hardening that only some installations need | Document least-privilege as a known limitation |
| U-16, U-17, U-19, U-20 | Split Settings page, unsaved-change warnings, keyboard shortcuts, dark mode and print stylesheet | P3 | Polish | After the reviewer workflow |

## Risks of deferring

| Deferred P1 | Risk | Known-limitation entry needed |
|---|---|---|
| F-3 SFTP | Customers whose server offers SFTP only can't use the FTP ingestion path | Yes, UAT section 5 |
| F-5 (rest), S-7 (rest) | They are release gates, not skippable. They must be scheduled before the release date | No limitation; plan the dates |

P2 items in LATER that a customer's security review is likely to raise: S-8 (`'unsafe-inline'`) and S-14 (alerting). List both as known limitations if they are still open at release.

---

## Closed in the previous version (not triaged)

| ID | Item | How it was closed |
|---|---|---|
| S-1 | Client IP in the login log can be forged | `9399a25`: `ForwardedHeadersSetup` honours forwarded headers only from configured proxies, the login log no longer reads `X-Forwarded-For` *(re-checked)*. The API and MCP rate-limit part of S-13 is still open and is in row 11 |
| S-4 | Session timeout and automatic logoff | Resolved 2026-11-06, TC-811 |
| U-1, U-3, U-5 | Self-registration, Logs menu roles, Error page | Resolved 2026-11-05/06, TC-791, TC-810, TC-809 |
| U-10 | Page titles, active menu item, shared breadcrumb | `eda133c` (from the commit; not re-checked in a browser) |
| U-18 | Responsive navbar and scrolling tables | `3334c26` (from the commit; not re-checked in a browser) |
| F-3 (part) | FTPS with certificate validation | `4f3ecb4`, 2026-11-09; SFTP is in LATER |
| F-13, F-18 | SBOM, release notes, docs alignment, housekeeping | Done in the sources |
| F-17 (part) | MCP query tools, per-user keys | `cde392c` |

### Where the sources are out of date

- `Path to v1.0 - Cybersecurity.md` still lists S-1 as open, and `Path to v1.0 - UX and UI.md` still lists U-10 and U-18 as open.
- `Path to v1.0.md` item 17 still says the MCP server has two tools and a static key, but `cde392c` added query tools and per-user keys.
- U-12 (naming) is partly closed by `10d6504` (one product name); the hard-coded footer version remains (row 14).

Fixing these is a documents update, not a triage decision.
