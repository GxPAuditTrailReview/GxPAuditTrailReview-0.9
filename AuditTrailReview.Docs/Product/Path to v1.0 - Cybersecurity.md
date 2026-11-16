# Path to v1.0 - Cybersecurity

| Field | Value |
|---|---|
| Product | GxPAuditTrailReview (all components) |
| Baseline | v0.9 (in development), `master` @ `23445c1` |
| Prepared | 2026-11-05 |
| Updated | 2026-11-06: item 4 resolved, item 7 partly resolved (threat model drafted), cross-references updated for the sign-off, Logs and Error page fixes |
| Status | Draft, for prioritisation |
| Companions | [Path to v1.0.md](Path%20to%20v1.0.md) (functional and compliance), [Path to v1.0 - UX and UI.md](Path%20to%20v1.0%20-%20UX%20and%20UI.md) |

## Purpose

This is the security list for v1.0. It covers the WebApp, Integration Hub API, Worker Service, MCP server, Console client, the Docker and pipeline set-up, and the data the product sends to third parties. It is based on a review of the source code and configuration, plus a NuGet vulnerability scan (`dotnet list package --vulnerable --include-transitive`, 2026-11-05: no known vulnerable packages). It is not a penetration test. Item 7 asks for one before release.

Items marked **(verified)** were confirmed in the source on the date above. Security items already on the other two lists are only cross-referenced in the [table below](#already-tracked-in-the-other-lists), not repeated.

## Priorities

- **P1 - Required for v1.0:** an attacker without special access can damage data integrity, availability or confidentiality, or a GxP expectation (Part 11 / Annex 11) isn't met.
- **P2 - Should be in v1.0:** defence in depth, or a gap a customer's security review will raise.
- **P3 - Can follow v1.0:** hardening and process improvements.

---

## Summary

| # | Item | Area | Priority |
|---|---|---|---|
| 1 | Client IP in the login log can be forged | Audit integrity | P1 |
| 2 | Anyone can permanently deactivate an account | Availability | P1 |
| 3 | Seeded admin password is public | Authentication | P1 |
| 4 | Session timeout and automatic logoff (**resolved**) | Session management | P1 |
| 5 | Docker set-up exposes the database and plain-HTTP ports | Deployment | P1 |
| 6 | Secrets management and the master encryption key | Secrets | P1 |
| 7 | Security testing and threat model before release (**partly resolved**) | Assurance | P1 |
| 8 | Tighten the Content Security Policy | Web hardening | P2 |
| 9 | Rate-limit the WebApp login | Authentication | P2 |
| 10 | Persist and protect Data Protection keys | Session management | P2 |
| 11 | Security checks in CI/CD | Supply chain | P2 |
| 12 | Control what audit-trail data goes to OpenAI | Data protection | P2 |
| 13 | API and MCP key storage and rate limiting behind a proxy | API security | P2 |
| 14 | Security event logging and alerting | Monitoring | P2 |
| 15 | Lock in safe parsing of uploaded files | Input handling | P2 |
| 16 | Password policy extras | Authentication | P3 |
| 17 | Least-privilege database accounts per component | Database | P3 |
| 18 | Vulnerability disclosure and security documentation | Process | P3 |

---

## P1 - Required for v1.0

### 1. Client IP in the login log can be forged (verified)

`Engine/Services/UserActivityLogService.cs` (around line 44) takes the IP address recorded for every login, failed login and lockout from the raw `X-Forwarded-For` request header if one is present, and only falls back to the connection's address. Anyone can send that header, so the IP in `Log → User login log` is whatever the client claims. In addition, the WebApp's `UseForwardedHeaders` set-up clears `KnownNetworks`/`KnownProxies` (`Program.cs` around line 241), so forwarded headers are accepted from any sender, not just the reverse proxy. The login log is GxP evidence, and an investigator can't trust it today.

To do:
- Record `HttpContext.Connection.RemoteIpAddress` only, after `UseForwardedHeaders` has processed the headers. Don't read `X-Forwarded-For` directly.
- Configure the reverse proxy's address or network in `KnownProxies`/`KnownNetworks` (as a setting, with the VPS and Docker docs updated) instead of clearing them. That also stops a direct, unproxied request from claiming `X-Forwarded-Proto: https`, which is how a forged header gets past the Secure-cookie check.
- Add a unit test that a forged `X-Forwarded-For` from an untrusted sender is ignored.

### 2. Anyone can permanently deactivate an account (verified)

After the configured number of failed logins (default 5), `FailedLoginLockoutService.EscalateToDeactivatedAsync` sets `LockoutEnd = DateTimeOffset.MaxValue`, and only an Admin can undo that. The only exception is the sole active Admin. User names are e-mail addresses, so anyone who knows or guesses a colleague's address can lock them out indefinitely by typing a wrong password five times. That includes every Admin except the last one. It is a cheap denial-of-service against reviewers, for example just before a release deadline.

To do (decide with QA which combination the SOPs need):
- Use a time-based lockout that grows with each repeat (for example 5 min, 15 min, 1 h) instead of immediate permanent deactivation. Keep permanent deactivation as a deliberate Admin action.
- Combine it with per-IP and per-account login rate limiting (item 9), so a single source can't lock out many accounts.
- Alert Admins when an account is locked (item 14), and show the reason on the Users page.

### 3. Seeded admin password is public (verified)

`AuditTrailReview.WebApp/appsettings.json` (tracked in git) contains `SeedAccount:Password`, a fixed, readable value. `Program.cs` (around line 1080) creates `admin@yourcompany.com` with that password the first time it runs against an empty database. Nothing forces a change at first login. Any installation where the installer forgot to override it has a full Admin account with a password published in this repository.

To do:
- Remove the password from the tracked file. Require it as a deployment secret (environment variable or user secrets), or generate a random one on first run and write it once to the console or log.
- Add a "must change password at next sign-in" flag, set for the seeded account and for accounts an Admin creates or resets, and enforce it at login.
- Have startup log an error, and the Admin panel show a warning, while the seeded account still has its initial password.

### 4. Session timeout and automatic logoff (verified)

> **Resolved 2026-11-06.** The Security tab (`Admin Panel → Settings`) now sets an idle timeout (default 20 minutes), a maximum session length (default 10 hours) and when the sign-out warning appears (default 2 minutes before the idle timeout). `Services/SecurityCookieSetup.cs` applies them to the Identity cookie: `ExpireTimeSpan` with `SlidingExpiration`, plus a session start time stamped into the ticket and checked against the maximum on every request. `wwwroot/js/session-timeout.js` shows the "You'll be signed out in …" dialog with "Stay signed in", then signs the user out and goes to Login; background notification polling no longer keeps an idle session alive. "Remember me" is removed from Login and LoginWith2fa (`isPersistent: false`). The security stamp is checked every minute, and the admin's Edit User page calls `UpdateSecurityStampAsync` on role changes and deactivation, so those and password changes end the user's other sessions within about a minute. Needs schema script `ATR 0.38` (three new `SecuritySettings` columns). Covered by TC-811 and new tests in TC-703; User Manual and v0.9 release notes updated.

`SecurityHeaders.ConfigureApplicationCookie` sets only `HttpOnly` and `SecurePolicy`, so ASP.NET Core Identity's default applies: a 14-day sliding cookie. With "Remember me" ticked the cookie is persistent. A reviewer who walks away from an unlocked PC stays signed in, with sign-off rights, for up to two weeks. Annex 11 §12 and the Part 11 guidance on controls for open sessions expect an inactivity timeout.

To do:
- Make the idle timeout configurable on the Security tab (for example default 15-30 minutes), with `SlidingExpiration` and an absolute maximum session length (for example 8-12 hours).
- Remove "Remember me", or cap it at the same absolute maximum.
- Add a client-side warning ("You'll be signed out in 2 minutes") and an automatic redirect to Login.
- Invalidate sessions when a password changes or an account is deactivated, through `UpdateSecurityStampAsync` with a short `SecurityStampValidator` interval.

### 5. Docker set-up exposes the database and plain-HTTP ports (verified)

`docker/docker-compose.postgres.yml` publishes PostgreSQL as `"5432:5432"` (line 49) on every network interface of the host, protected by the placeholder `POSTGRES_PASSWORD` until someone changes it. The app ports (`8080`, `8081`, `8082`) are also published on all interfaces over plain HTTP. On the documented VPS deployment, a single missing firewall rule exposes the database to the internet, and lets direct requests reach the WebApp, which trusts forged `X-Forwarded-*` headers (item 1).

To do:
- Don't publish 5432 at all (the containers reach it over the compose network), or bind it to `127.0.0.1:5432:5432`.
- Bind the app ports to `127.0.0.1` and put the reverse proxy (TLS) in front.
- Refuse to start when `POSTGRES_PASSWORD` is still the placeholder, through a check in the compose file or an entrypoint script.
- Document the firewall rules and TLS termination as required, not optional, in `CICD/Run docker compose on VPS.md`.

### 6. Secrets management and the master encryption key (partly verified)

One AES-256-GCM master key (`Security:EncryptionKey`) protects the OpenAI key and every API and MCP key. Every component reads it from its `appsettings.json`, which ships with the placeholder `CHANGE_ME_32_BYTE_BASE64_KEY`. SMTP and FTP passwords are also plain configuration values. Problems:
- Nothing checks at startup that the placeholder has been replaced. The failure only shows up later as a 500 or as every key check failing (v0.9 release notes).
- There is no documented way to rotate the master key. Changing it makes every stored key unreadable.
- The key and the passwords sit in plain files on every server that runs a component.

To do:
- Fail fast at startup with a clear message if the key is missing, still the placeholder, or not 32 bytes.
- Support an external secret store (Azure Key Vault, Windows DPAPI-protected file, Docker/Kubernetes secrets, environment variables), and document a recommended option for each deployment type.
- Version the key (store a key ID with each ciphertext) so the key can be rotated: decrypt with the old key, re-encrypt with the new one, from an Admin action or a CLI.
- Write a secrets inventory (which component needs which secret, where it is stored) for the Installation Manual.

### 7. Security testing and threat model before release

> **Partly resolved 2026-11-06.** A STRIDE threat model is drafted in [`AuditTrailReview.Docs/Security/Threat Model.md`](../Security/Threat%20Model.md) (under `Security/` rather than `Tech/`). It covers every component and data flow listed below and maps each gap to an item on this list. It is still a draft, to be reviewed before the release candidate. **Still to do:** the ZAP scan, the external penetration test, and the security section in the Risk Assessment.

No threat model, penetration test or dynamic scan is documented. A GxP customer's IT security review and supplier assessment will ask for one.

To do:
- A short threat model (STRIDE over the four components and their data flows: browser, WebApp, database, Worker/FTP, API/Console, MCP/chat client, OpenAI), kept in `AuditTrailReview.Docs/Tech/`.
- An authenticated OWASP ZAP (or similar) scan of the WebApp and API, with findings fixed or accepted.
- An external penetration test of the v1.0 release candidate. Record the report reference in the Validation Report.
- A security section in the Risk Assessment that links to the threat model.

---

## P2 - Should be in v1.0

### 8. Tighten the Content Security Policy (verified)

`SecurityHeaders.ContentSecurityPolicy` allows `script-src 'self' 'unsafe-inline'`, plus `style-src`/`font-src` from `cdn.jsdelivr.net`. `'unsafe-inline'` removes most of CSP's protection against cross-site scripting. The pages rely on inline `<script>` blocks (for example the notification poller in `_Layout.cshtml`) and inline `onclick=` handlers (for example `openAssignModal(...)` on the Scans list).

To do: move inline scripts into files under `wwwroot/js` (or use per-request nonces), replace inline event handlers with `addEventListener`, then drop `'unsafe-inline'` from `script-src`. Self-host Bootstrap Icons (UX/UI list item 13) and remove the jsDelivr host. Until then, add Subresource Integrity (`integrity=`) to the CDN link.

### 9. Rate-limit the WebApp login (verified)

The Integration Hub API and the MCP server have ASP.NET Core rate limiters. The WebApp has none. Login, Forgot password and Reset password can be called as fast as the server responds. Combined with item 2, that makes mass lock-outs easy, and password spraying (one common password tried against many accounts) never triggers a per-account lockout.

To do: add `AddRateLimiter` to the WebApp with a per-IP limit on `/Identity/Account/Login`, `ForgotPassword` and `ResetPassword` (after item 1, so the IP is real). Log and alert when the limit is hit.

### 10. Persist and protect Data Protection keys (verified)

None of the components configures `AddDataProtection`. On Windows/IIS the keys go to the user profile. In the Docker images they live inside the container and are lost on every restart or redeploy, which signs everyone out and invalidates open forms. Two WebApp instances behind a load balancer can't read each other's cookies. The keys are also not encrypted at rest.

To do: persist the key ring to a shared location (the database through `PersistKeysToDbContext`, or a mounted volume), protect it (`ProtectKeysWithCertificate` or DPAPI), and set an explicit application name.

### 11. Security checks in CI/CD (verified)

`pipelines/pipeline-ci-build-unit-test-dev-ops.yml` builds and runs the unit tests only. Today's scan found no vulnerable packages, but nothing re-checks that.

To do:
- Fail the build on `dotnet list package --vulnerable --include-transitive`.
- Secret scanning (for example gitleaks) on every push. The PRD's own history mentions a live OpenAI key once committed to `appsettings.Development.json`.
- Container image scanning (for example Trivy) in `pipelines/pipeline-deploy-on-azure-cloud.yml`, and pin base images by digest.
- Dependabot or Renovate for NuGet, the front-end libraries in `wwwroot/lib` and the Docker images.
- Generate the SBOM (CycloneDX) in the pipeline instead of by hand (main list item 13).

### 12. Control what audit-trail data goes to OpenAI (verified)

After every scan, the AI step sends either the whole result set (up to 300 rows by default) or a sample of findings plus a statistical summary to the OpenAI API (`Engine/AI/OpenAIService.cs`). That data includes user names, record identifiers and old and new values from regulated systems. The code doesn't redact anything. This is a data transfer to a third party (and possibly to another country), and the customer's data-protection and GxP data-integrity owners need to approve it.

To do:
- Make "off" the default on a new installation. Today `Program.cs` seeds `AiAnalysisEnabled = true`, so the first scan already sends data before anyone has approved it.
- Add an option to pseudonymise user names and identifiers before sending, mapping them back in the displayed result.
- A provider and endpoint can already be chosen (`AiSettings.Endpoint`, OpenAI or Microsoft Foundry, see `OpenAIService.ResolveEndpoint`), which allows an EU-hosted deployment. Document this as the recommended set-up for regulated data, and show the endpoint in use on the AI Analysis tab.
- Document exactly which fields are sent, and the provider's retention terms, in the User Manual and Technical Manual. Show a short notice on the AI Analysis settings tab.

### 13. API and MCP key storage and rate limiting behind a proxy (partly verified)

- API and MCP keys are stored **encrypted**, so they can be decrypted, with the master key, and compared in constant time (`ApiKeyCredentialValidator`, `FixedTimeEquals`). These are random keys, so storing only a SHA-256 hash of each one would be enough, and a leaked database plus master key would then not reveal any working key. Consider moving to hashes for v1.0, together with expiry, scopes and "last used" (main list item 10).
- Both services partition the rate limiter by `Connection.RemoteIpAddress`. Neither calls `UseForwardedHeaders`, so behind a reverse proxy every caller shares the proxy's address and one bucket: one busy client throttles all the others. Configure forwarded headers with a known-proxy list (as in item 1), or partition by API key, which the API already does once a key is presented.
- The MCP server has no `UseHttpsRedirection`/`UseHsts`, and its compose service is plain HTTP on 8082. Require TLS termination in front of it (ADR-0010 already says so) and document it as mandatory.

### 14. Security event logging and alerting

Logins, failed logins and lockouts are written to `dbo.UserActivityLog`, and requests to `dbo.ApplicationLog`, but nothing alerts anyone.

To do:
- Define the security events: failed logins over a threshold, an account locked or deactivated, API/MCP 401 bursts, requests to disabled pages such as Register, changes to roles or settings, AI key changes.
- Send an e-mail to Admins for the critical ones, and document how to forward the Serilog output to the customer's SIEM.
- Make sure the application's own database login can't delete or change log rows (see item 17 and main list item 2).

### 15. Lock in safe parsing of uploaded files (verified, currently safe)

Uploaded files come from outside the organisation's control. Today:
- XML is parsed with `XDocument.Parse`. In .NET this prohibits DTDs by default, so XXE and entity-expansion attacks are blocked.
- JSON uses `JsonDocument.Parse` with the default maximum depth of 64.
- Excel exports set cell values as text, not formulas.
- The original-file download is sent as an attachment with `X-Content-Type-Options: nosniff`.

None of this is asserted anywhere, so a refactor could silently undo it. Make the settings explicit (`XmlReaderSettings { DtdProcessing = Prohibit, XmlResolver = null }`, explicit `JsonDocumentOptions.MaxDepth`) and add unit tests with an XXE payload, a "billion laughs" file, a very deeply nested JSON file, and a CSV value starting with `=`/`+`/`-`/`@` that must come out of the Excel export as plain text.

---

## P3 - Can follow v1.0

### 16. Password policy extras

The policy (length, character classes, lockout threshold) is already configurable on the Security tab. Still missing: a check against a list of breached or common passwords (offline list, no external call), an optional maximum password age for customers whose SOPs require one, and a password history so users can't reuse their last N passwords.

### 17. Least-privilege database accounts per component

All four components use one connection string, and therefore one database login with full rights. For v1.0, document (and ship SQL scripts for) a separate login per component:
- **WebApp:** full rights to its tables, but no `UPDATE`/`DELETE` on the signature and audit log tables.
- **API:** inserts into `ScanUploadJob` and reads the key tables only.
- **Worker Service:** its job, scan and ingestion tables only.
- **MCP server:** read-only.

This also limits the damage if any one component is compromised.

### 18. Vulnerability disclosure and security documentation

Add a `SECURITY.md` to the repository and a `/.well-known/security.txt` to the WebApp, saying how to report a vulnerability and what response times to expect. Add a "Security configuration checklist" to the Installation Manual: TLS, proxy addresses, secrets, firewall, seed account, AI on or off, least-privilege logins. That gives customers one page to sign off during installation qualification.

---

## Already tracked in the other lists

| Topic | Where |
|---|---|
| Password re-entry and signature meaning at sign-off (Part 11 §11.200) | Password re-entry done in `405a3a2` (TC-783; wrong passwords count toward lockout). Signature meaning still open: main list item 1, UX/UI list item 2 |
| Tamper evidence for signatures and audit trail (hash chain) | Main list item 2 |
| FTPS / SFTP for the Worker Service | Main list item 3 |
| Self-registration and unused Identity pages | Fixed in `85151fd` (TC-791). Follow-ups: main list item 6 |
| API key expiry, scopes, last-used | Main list item 10 |
| Logs menu visible to every role | Fixed in `8f7d5d5` (TC-810): Admin and Super User only. UX/UI list item 3 |
| Error page showing template "Development Mode" text | Fixed in `aeecf35` (TC-809), Privacy page removed. UX/UI list item 5 |
| Bootstrap Icons loaded from a CDN | UX/UI list item 13 |

## Checked and found in order

The following were checked during this review, and no change is needed for v1.0:
- **NuGet packages:** no known vulnerable packages in any project, including transitive ones (scan of 2026-11-05).
- **Front-end libraries:** jQuery 3.7.1 and Bootstrap 5.3.3 are current, with no open advisories known at review time.
- **SQL:** all raw SQL in `ScanFileReader` and `ScanUploadJobProcessor` is parameterised. EF Core handles the rest.
- **Key comparison:** API and MCP key checks compare in constant time (`CryptographicOperations.FixedTimeEquals`).
- **Headers, cookies and CSRF:** `X-Frame-Options: DENY`, `frame-ancestors 'none'`, `nosniff`, a referrer policy, `Permissions-Policy`, COOP, Secure and HttpOnly cookies, antiforgery tokens on forms, and HSTS and HTTPS redirection outside Development (WebApp and API). The `Server` header is removed.
- **Authorization by default:** every page under `Pages/` requires sign-in by default (`AuthorizeFolder("/")`, guarded by `PageAuthorizationConventionTests`), and `/Account/Manage` too since `85151fd`.
- **Containers:** all four Dockerfiles switch to a non-root `USER`.
- **Swagger/OpenAPI:** served in Development only (ADR-0005).
- **Notification bell:** HTML-escapes file names and error messages before inserting them into the page.
