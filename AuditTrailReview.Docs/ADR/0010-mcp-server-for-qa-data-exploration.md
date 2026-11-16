# 0010. Read-only MCP server for QA data exploration, kept separate from the Integration Hub API

Status: Accepted
Date: 2026-11-04

## Context
QA testers and reviewers often need to look at scan/system data ad hoc -- "what scans exist for
this system," "what did the last few uploads look like" -- without writing SQL against the
shared database directly or navigating the WebApp's UI for a quick, exploratory look. At the
same time, `AuditTrailReview.IntegrationHub.API` (ADR-0005) already exists as a small,
auditable, API-key-authenticated surface whose entire reason to exist is letting external
systems and scripts push scan files *in*. Its audience (scripted integrators), its auth model
(a small set of static, long-lived keys), and its deliberately narrow scope (one upload
endpoint plus a status check) are all tuned for that one job. Growing it with unrelated
read/query endpoints for a different audience -- a human exploring data conversationally
through an LLM chat client, not a script pushing files -- would blur that purpose and expand a
production-facing, write-capable component's surface for a need that has nothing to do with
ingestion.

Separately, the Model Context Protocol (MCP) had become a practical way to let a chat client
(for example, Claude) call into an application's own data directly, instead of a person
manually running SQL queries or screen-navigating a report. The official `ModelContextProtocol`
/ `ModelContextProtocol.AspNetCore` C# SDK makes this straightforward to host inside an
ASP.NET Core process, the same way every other hostable component in this solution is already
built and shipped (ADR-0001).

## Decision
Add a new component, `AuditTrailReview.McpServer`, as its own project rather than extending
`AuditTrailReview.IntegrationHub.API`:

- References only `AuditTrailReview.Engine` (same pattern as `AuditTrailReview.IntegrationHub.WorkerService`), which carries `AuditTrailReview.Database` (`ApplicationDbContext`) and `AuditTrailReview.Model` in transitively -- no separate data-access layer.
- Exposes MCP "tools" as plain static methods (`[McpServerToolType]` / `[McpServerTool]` in `Tools/AuditTrailTools.cs`) that query `ApplicationDbContext` directly with `AsNoTracking()`. As of this writing there are six: `get_scan_summary`, `get_scan_findings`, `query_scan_records` and `query_app_audit_trail` (added 2026-11-07, same read-only/200-row-cap rules) alongside the original `list_systems` (every configured `SystemATR` row) and `list_scans` (most recent scans, optionally filtered by `systemId`, capped at 200 rows per call -- the cap exists to protect the calling chat client's own context window from an accidental full-table dump, not as a security control).
- **Strictly read-only by construction**: nothing in this project ever calls `SaveChanges`. Unlike the Integration Hub API, there is no code path here that can write to the database at all, regardless of what a chat client or a malicious prompt asks it to do.
- Hosted over HTTP using the SDK's Streamable HTTP transport (`WithHttpTransport(...)`, `app.MapMcp("/mcp")`), with `HttpServerTransportOptions.Stateless = true`. Every tool here is a one-shot read with no server-initiated calls back to the client (no sampling/roots), so there is nothing that needs per-session state or an `Mcp-Session-Id` -- stateless mode also matches a deployment that might later sit behind a load balancer with no session affinity.
- Ships its own `Dockerfile`, mirroring `AuditTrailReview.IntegrationHub.API`'s build stages, for the same reason every other hostable component does (ADR-0001) -- and, **as of v1.0**, wired into `docker/docker-compose.postgres.yml` as the `mcp` service (port 8082).

## Consequences

**Positive:**
- QA/testers get conversational, ad hoc access to scan/system data through a chat client, without SQL access or a dedicated report page, and without touching `IntegrationHub.API`'s integrator-facing surface, auth model, or rate limiting at all.
- The read-only design means the worst a misbehaving or prompt-injected chat client can do through this component is read data it's allowed to read -- never change anything. This is a stronger guarantee than "the UI happens not to expose a button for it."
- Reuses the existing shared-database pattern (ADR-0002) exactly: just another `ApplicationDbContext` consumer, no new data-access abstraction to maintain.

**Negative / known gaps:**
- **Static API key and a per-IP rate limit protect `/mcp`.** `McpApiKeyMiddleware` rejects any `/mcp` request (401) that does not carry a configured key in `X-Api-Key` (header name configurable) or as `Authorization: Bearer <key>`; ~~it fails closed when no key, or only the shipped placeholder `CHANGE_ME_MCP_API_KEY`, is configured~~. A fixed-window limiter (`McpRateLimiting`, default 60 requests per 60 s per caller IP, 429 when exceeded) runs before the key check so bad-key guessing is throttled too. `GET /health` is unauthenticated and exempt from the limiter, for container-platform probes. Limits of this design: ~~a static key carries no per-user identity~~ **Changed, 2026-11-07:** `dbo.McpApiKeyCredential.UserId` (ATR 0.39, nullable, deliberately not a foreign key) lets an admin issue a key to a WebApp user from the MCP tab. `McpApiKeyMiddleware` resolves which key matched, sets `HttpContext.User` (name = the user's username, or the key name for a shared key, plus key-id claims) and logs one attributed line per request (every tool call is one stateless POST). Attribution is in the console log only; there is no persisted MCP access-log table yet, and behind a reverse proxy/ingress the limiter sees the proxy's address unless forwarded headers are configured, so it acts as one global limit. ~~The key should come from a secret store (e.g. Key Vault), distinct from the Integration Hub API's keys, and the database login used by this component should be read-only so the boundary does not rest on the code alone.~~ **Changed, 2026-11-05.** The static `McpAuth:Keys` config list (and its shipped placeholder) is gone. Keys now live encrypted (`ApiKeyProtector`, AES-256-GCM) in a new `dbo.McpApiKeyCredential` table, generated and revoked from the WebApp's `AdminPanel -> Settings -> MCP` tab; the middleware queries that table per request (resolving the scoped `ApplicationDbContext` as an `InvokeAsync` parameter rather than at construction, since the middleware instance itself is a singleton), so a revoked key stops working immediately with no restart. "Distinct from the Integration Hub API's keys" is no longer just a deployment recommendation: the two services draw from structurally separate tables (`McpApiKeyCredential` here, `ApiKeyCredential` for the API) that share only the decrypt/compare logic (`AuditTrailReview.Engine.Security.ApiKeyCredentialValidator`), never each other's actual keys. The database login being read-only remains a deployment-time choice, not enforced by this change.
- ~~**SQL Server only.**~~ **Changed.** `Program.cs` honours `Database:Provider` (`SqlServer` default, `Postgres` via `appsettings.Postgres.json`), same as `IntegrationHub.API`/the Worker Service.
- **No Serilog / `dbo.ApplicationLog` integration.** It uses only the default ASP.NET Core console logger, so (unlike the other four components) its activity isn't visible through the `dbo.ApplicationLog` operational runbook (`Technical Manual.md` section 6) -- console output only.
- ~~**No health-check endpoint**, matching the other four components' current gap.~~ **Changed:** `GET /health` returns 200 without a key and is exempt from the rate limiter (`Program.cs`). The other four components still have no health endpoint.
- **The local ASP.NET Core HTTPS dev certificate is not generally trusted by MCP client TLS stacks.** Confirmed during development: a browser accepted it, but Claude Code's own MCP client failed with a generic `fetch failed` against the same certificate. Local testing therefore uses the plain HTTP endpoint (`http://localhost:59162/mcp` in the dev `launchSettings.json`) instead of HTTPS; a real network deployment needs a properly issued certificate, not the dev cert, the same as any other hosted component.
- **Not browsable like a REST API with Swagger.** MCP over Streamable HTTP speaks JSON-RPC over `POST` only; a bare `GET` (what typing the URL into a browser does) correctly returns `405 Method Not Allowed`. Exploring what tools exist and calling them interactively needs an actual MCP client or the `@modelcontextprotocol/inspector` dev tool, not a browser address bar.

## Alternatives considered
- **Add read-only `GET` endpoints to `AuditTrailReview.IntegrationHub.API` instead of a new project.** Rejected: that component's entire reason to exist (ADR-0005) is a small, auditable, write-only surface for scripted integrators under API-key auth. Bolting on an unrelated read/exploration surface for a completely different audience and access pattern (a human, via an LLM chat client) would blur its purpose and grow a production, write-capable component's attack surface for a need unrelated to ingestion.
- **stdio transport** (the server launched as a per-session child process directly by the chat client, with no network hosting at all) instead of HTTP. Simpler and needs no deployment for a single QA tester running it entirely on their own machine, but doesn't support one shared instance that multiple people connect to -- which is the actual goal once a network-hosted variant was decided on. HTTP (Streamable HTTP, stateless) was chosen instead; stdio remains a reasonable choice for someone who only ever wants to run this for themselves.
- **Giving the MCP tools write access** (for example, a tool to re-run a scan or change a finding's review status) was deliberately not done in this pass. Keeping every tool a pure, non-tracked read keeps this component's blast radius at "can read everything it's allowed to," never "can change anything" -- a stronger and simpler guarantee than relying on authorization checks per write-capable tool.
