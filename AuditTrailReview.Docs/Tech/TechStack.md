# Tech Stack v0.9 

This document summarizes the current tech stack and the projects present in the solution.

## Platform

| Layer | Technology | Version |
|-------|-----------|---------|
| Runtime | .NET | 10 (net10.0), consistent across all 11 projects |
| Language | C# | latest (project `LangVersion` where set explicitly) |
| Web framework | ASP.NET Core Razor Pages / Web API | 10 |
| Background service | .NET Worker Service (`Microsoft.NET.Sdk.Worker`) | 10 |
| Database | SQL Server (LocalDB for local dev) is the supported database. SQLite and PostgreSQL 16 are available only in the evaluation Docker flavors (`Database:Provider`) | N/A |
| Containerization | Docker (Linux images) | N/A |

---

## Projects (solution overview)

- **AuditTrailReview.WebApp**: Razor Pages web application (UI, ASP.NET Core Identity, EF Core). Ships a `Dockerfile`.
- **AuditTrailReview.IntegrationHub.API**: ASP.NET Core Web API for integration scenarios. Ships a `Dockerfile`.
- **AuditTrailReview.IntegrationHub.WorkerService**: hosts the Integration Hub Worker Service (project file `AuditTrailReview.IntegrationHub.WorkerService.csproj`, `Microsoft.NET.Sdk.Worker`) plus the FTP-polling connector logic. Ships a `Dockerfile`. (The project's folder was itself named `AuditTrailReview.Connector` until the Path to v1.0.md Thirtieth follow-up renamed it to match the `.csproj`/namespace; a stale, empty leftover folder that collided with the new name was deleted as part of that rename.)
- **AuditTrailReview.Database**: EF Core `ApplicationDbContext` and shared data-access code, referenced by every component that touches the database.
- **AuditTrailReview.Engine**: core business logic, including file parsing (CSV/XML/JSON), header-keyword mapping, the 19-rule review engine, OpenAI-based AI analysis, and shared services (email, user activity logging, internal audit-trail logging).
- **AuditTrailReview.Model**: shared domain model types (POCOs only, no package dependencies).
- **AuditTrailReview.Console**: cross-platform CLI client that submits a local file to the Integration Hub API. No NuGet dependencies (plain `System.Net.Http.HttpClient`) and **no `Dockerfile`**; it's meant to run as a short-lived CLI invocation via `dotnet run` / `dotnet AuditTrailReview.Console.dll`, not as a hosted service.
- **AuditTrailReview.McpServer**: ASP.NET Core host exposing a read-only Model Context Protocol (MCP) server (`ModelContextProtocol.AspNetCore`) over Streamable HTTP (stateless mode), for QA to explore scan/system data from a chat client (e.g. Claude). References only `AuditTrailReview.Engine` (same as the Worker Service). Ships a `Dockerfile`; `/mcp` is protected by a static API key and a per-IP rate limit, with an unauthenticated `/health` probe (see ADR-0010).
- **AuditTrailReview.UnitTest**: unit tests (MSTest).
- **AuditTrailReview.UISeleniumTest**: UI tests using Selenium WebDriver (MSTest).
- **AuditTrailReview.Docs**: this documentation project.

