# 0001. Modular multi-project .NET solution

Status: Accepted
Date: 2026-09-01 (reconstructed from the v0.6 codebase)

## Context
The product needs to run in more than one shape: an interactive web application for reviewers, a background service that polls FTP for automated ingestion, an HTTP API for external/scripted submission, and a CLI client for one-off submissions. All of these need to share the same parsing, mapping, rule-evaluation, and data-access logic, or that logic would drift between paths.

## Decision
Split the solution into focused projects with a clear dependency direction:

- `AuditTrailReview.Model`: domain types, depended on by everything.
- `AuditTrailReview.Database`: EF Core `DbContext`, depended on by everything that touches data.
- `AuditTrailReview.Engine`: parsing, mapping, rule engine, AI analysis, shared services; depends on Model and Database.
- `AuditTrailReview.WebApp`, `AuditTrailReview.IntegrationHub.API`, `AuditTrailReview.IntegrationHub.WorkerService` (Worker Service), and `AuditTrailReview.Console`, the four "front doors," each depending on Engine (directly or, for the Console client, indirectly via the API over HTTP).
- `AuditTrailReview.UnitTest` and `AuditTrailReview.UISeleniumTest`: test projects.
- `AuditTrailReview.Docs`: documentation, versioned alongside the code.

All ten projects target `net10.0` uniformly.

## Consequences

**Positive:**
- The rule engine and parsing logic are genuinely shared, so a rule change in `AuditTrailReview.Engine` applies identically to the WebApp, the Worker Service, and the API in one place, for the *rule evaluation* itself.
- Each front door can be deployed, scaled, and versioned independently (each of WebApp/API/Worker Service ships its own `Dockerfile`).
- Test projects can reference `Engine` directly without pulling in ASP.NET Core hosting concerns.

**Negative / known gaps:**
- **File-parsing is not actually shared.** Despite the modular structure, CSV/XML/JSON row-extraction logic is implemented three separate times: in `Pages/Scan/Scan.cshtml.cs` (manual upload), `Pages/Scan/Preview.cshtml.cs` (preview page), and `AuditTrailReview.Engine/ScanProcessingService.cs` (used by the Worker Service and API). A parsing fix applied to one copy can silently be missed in the other two. This should be consolidated into `Engine` as the single source of truth.
- There is a legacy, now-empty `AuditTrailReview.IntegrationHub.WorkerService` project folder still present in the solution alongside `AuditTrailReview.IntegrationHub.WorkerService`, which is where the Worker Service's actual source lives today; this is a cleanup item, not a functional risk.
- No automated tests exist for the WebApp's Razor Page handlers or the Integration Hub API controller. Only `Engine` has unit test coverage (`AuditTrailReview.UnitTest`).

## Alternatives considered
- **A single monolithic ASP.NET Core project** hosting both the UI and the API endpoints, with the Worker Service as a hosted `IHostedService` inside it. Simpler to deploy as one unit, but would prevent independently scaling or restarting the FTP poller without restarting the web UI, and would couple the Worker Service's uptime to the web app's.
- **A microservice-per-rule-category** split of the Engine. Rejected as unnecessary complexity for a rule set that's currently small (21 rules) and evaluated in-process against an in-memory row set.
