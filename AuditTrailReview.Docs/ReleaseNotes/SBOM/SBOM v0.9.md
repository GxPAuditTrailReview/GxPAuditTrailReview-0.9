# Software Bill of Materials (SBOM) – GxPAuditTrail v0.9

| | |
|---|---|
| **Product** | GxPAuditTrail – Audit Trail Review |
| **Release** | v0.9 |
| **Source revision** | `5f039b5` (branch `master`; regenerate if the release is cut from a later commit) |
| **SBOM generated** | 2026-11-14 |
| **Product license** | Business Source License 1.1 (see `LICENSE` at the repository root; Licensor: Axteon sp. z o.o.) |
| **Format** | Human-readable Markdown inventory (not SPDX / CycloneDX) |
| **Target framework** | .NET 10 (`net10.0`); SDK pinned in `global.json` (10.0.400, `rollForward: patch`); generated with SDK 10.0.404 |

This SBOM supersedes [SBOM v0.8](SBOM%20v0.8.md). Every change since v0.8 is covered here.

## 1. Scope and method

This document lists the third-party software built into or distributed with the five deployable components of v0.9:

| Component | Project | Deployment form |
|---|---|---|
| **WebApp** | `AuditTrailReview.WebApp` (Razor Pages + Identity) | Web application, Docker image |
| **API** | `AuditTrailReview.IntegrationHub.API` | HTTP API, Docker image |
| **Worker** | `AuditTrailReview.IntegrationHub.WorkerService` | Windows Service (Inno Setup installer in `installer/WorkerService/`), Docker image |
| **Console** | `AuditTrailReview.Console` | Command-line client |
| **MCP** | `AuditTrailReview.McpServer` (new since v0.8) | Read-only MCP server, Docker image |

`AuditTrailReview.Engine`, `AuditTrailReview.Database` and `AuditTrailReview.Model` are first-party libraries referenced by the components above; their NuGet dependencies are included in each component's closure. `AuditTrailReview.Docs` has no package dependencies.

The NuGet inventory was produced from a restored solution (`dotnet restore GxPAuditTrailReview.slnx`) using, per project:

```bash
dotnet list <project>.csproj package --include-transitive --format json
```

Versions are the **resolved** versions. License identifiers come from each package's `.nuspec` `<license>` element. Where a package declares a license file instead of an SPDX expression, the file is named in the table; these are Microsoft license terms that permit use and redistribution of the binaries within an application.

## 2. Summary

| Item | Count |
|---|---|
| Distinct runtime NuGet package versions (all five components) | 135 |
| Of which direct package references | 28 |
| Build/test-only package versions (not shipped) | 52 |
| Bundled client-side libraries (WebApp) | 4 |
| Known vulnerabilities in any package (section 7) | 0 |
| Deprecated packages in shipped components (section 7) | 0 |

License distribution of the runtime NuGet packages:

| License | Packages |
|---|---|
| MIT | 107 |
| Apache-2.0 | 20 |
| Microsoft Software License Terms (bundled `LICENSE.txt`) | 2 |
| PostgreSQL | 2 |
| MS-PL OR Apache-2.0 | 1 |
| BSD-2-Clause | 1 |
| Microsoft Software License Terms (bundled `LICENSE`) | 1 |
| Microsoft Software License Terms (bundled `EULA.md`) | 1 |

No GPL / LGPL / AGPL (copyleft) licenses are present in the runtime closure.

## 3. Platform and runtime

| Item | Version | License | Notes |
|---|---|---|---|
| .NET runtime / ASP.NET Core shared framework | 10.0.x | MIT | Provided by the host, or by the Docker base image `mcr.microsoft.com/dotnet/aspnet:10.0` |
| .NET SDK (build only) | 10.0.400 (`global.json`) | MIT | Build image `mcr.microsoft.com/dotnet/sdk:10.0`; not shipped |
| Docker base images | `mcr.microsoft.com/dotnet/aspnet:10.0`, `mcr.microsoft.com/dotnet/sdk:10.0` | MIT (.NET); OS layers carry their own licenses | Used by the WebApp, API, Worker and MCP Dockerfiles. Image digests are not pinned |
| Inno Setup | 6.x | Inno Setup License (free, incl. commercial use) | Build-time tool for the Worker Service installer; not shipped |

## 4. Client-side libraries (WebApp, `wwwroot/lib`)

| Library | Version | License | Location |
|---|---|---|---|
| Bootstrap | 5.3.8 | MIT | `wwwroot/lib/bootstrap` (also NuGet `bootstrap` 5.3.8) |
| jQuery | 3.7.1 | MIT | `wwwroot/lib/jquery` |
| jQuery Validation Plugin | 1.21.0 | MIT | `wwwroot/lib/jquery-validation` |
| jQuery Validation Unobtrusive | bundled (.NET Foundation and Contributors) | MIT | `wwwroot/lib/jquery-validation-unobtrusive` |

## 5. External services and optional dependencies (not bundled)

| Service | Used by | Purpose | Notes |
|---|---|---|---|
| AI provider: OpenAI API, Azure OpenAI or Microsoft Foundry (endpoint set on the AI Analysis tab) | WebApp / Engine (`Engine/AI/OpenAIService.cs`) | AI-supported audit trail analysis | Optional; needs an API key stored on the AI Analysis tab. The documentation chatbot and its embeddings model were removed after v0.8 |
| Microsoft SQL Server (incl. LocalDB) | All server components | Primary supported database | Customer-provided |
| PostgreSQL | Docker flavor (`docker/docker-compose.postgres.yml`) | Trial / functional-testing database | Not for production; via `Npgsql`. The same compose file also runs a `pg_dump` backup service |
| SQLite | Docker flavor (`docker/docker-compose.sqlite.yml`) | Trial / functional-testing database | Not for production; native library from `SQLitePCLRaw.lib.e_sqlite3` |
| FTP / FTPS server | Worker | Audit trail file drop folder (configured on the FTP tab; off by default) | Customer-provided; client library `FluentFTP` |
| MCP client (for example Claude) | MCP | Conversational read-only exploration of scan data | Customer-chosen; authenticates with an API key |

## 6. NuGet packages shipped with the components

"Direct" means a `PackageReference` in a shipped project (including `Engine` and `Database`); "Transitive" means pulled in by another package.

| Package | Version | License (SPDX / terms) | Type | Shipped in |
|---|---|---|---|---|
| `Azure.Core` | 1.50.0 | MIT | Transitive | MCP |
| `Azure.Core` | 1.51.1 | MIT | Transitive | WebApp, API, Worker |
| `Azure.Identity` | 1.17.1 | MIT | Transitive | MCP |
| `Azure.Identity` | 1.18.0 | MIT | Transitive | WebApp, API, Worker |
| `bootstrap` | 5.3.8 | MIT | Direct | WebApp |
| `ClosedXML` | 0.105.1 | MIT | Direct | WebApp |
| `ClosedXML.Parser` | 2.0.0 | MIT | Transitive | WebApp |
| `CsvHelper` | 33.1.0 | MS-PL OR Apache-2.0 | Direct | WebApp, API, Worker, MCP |
| `DocumentFormat.OpenXml` | 3.1.1 | MIT | Transitive | WebApp |
| `DocumentFormat.OpenXml.Framework` | 3.1.1 | MIT | Transitive | WebApp |
| `ExcelNumberFormat` | 1.1.0 | MIT | Transitive | WebApp |
| `FluentFTP` | 55.0.0 | MIT | Direct | Worker |
| `Markdig` | 1.4.0 | BSD-2-Clause | Direct | WebApp |
| `Microsoft.AspNetCore.Cryptography.Internal` | 10.0.13 | MIT | Transitive | Worker |
| `Microsoft.AspNetCore.Cryptography.KeyDerivation` | 10.0.13 | MIT | Transitive | Worker |
| `Microsoft.AspNetCore.Identity.EntityFrameworkCore` | 10.0.13 | MIT | Direct | WebApp, API, Worker, MCP |
| `Microsoft.AspNetCore.Identity.UI` | 10.0.13 | MIT | Direct | WebApp |
| `Microsoft.AspNetCore.OpenApi` | 10.0.13 | MIT | Direct | API |
| `Microsoft.Bcl.AsyncInterfaces` | 8.0.0 | MIT | Transitive | MCP |
| `Microsoft.Bcl.AsyncInterfaces` | 10.0.2 | MIT | Transitive | WebApp, API, Worker |
| `Microsoft.Bcl.Cryptography` | 9.0.21 | MIT | Transitive | WebApp, API, Worker |
| `Microsoft.Data.SqlClient` | 6.1.7 | MIT | Transitive | MCP |
| `Microsoft.Data.SqlClient` | 7.1.2 | MIT | Direct | WebApp, API, Worker |
| `Microsoft.Data.SqlClient.Extensions.Abstractions` | 7.1.2 | MIT | Transitive | WebApp, API, Worker |
| `Microsoft.Data.SqlClient.Extensions.Azure` | 7.1.2 | MIT | Direct | WebApp, API, Worker |
| `Microsoft.Data.SqlClient.Internal.Logging` | 7.1.2 | MIT | Transitive | WebApp, API, Worker |
| `Microsoft.Data.SqlClient.SNI.runtime` | 7.1.2 | Microsoft Software License Terms (bundled `LICENSE.txt`) | Transitive | WebApp, API, Worker |
| `Microsoft.Data.SqlClient.SNI.runtime` | 6.0.3 | Microsoft Software License Terms (bundled `LICENSE.txt`) | Transitive | MCP |
| `Microsoft.Data.Sqlite.Core` | 10.0.13 | MIT | Transitive | WebApp |
| `Microsoft.EntityFrameworkCore` | 10.0.13 | MIT | Direct | WebApp, API, Worker, MCP |
| `Microsoft.EntityFrameworkCore.Abstractions` | 10.0.13 | MIT | Transitive | WebApp, API, Worker, MCP |
| `Microsoft.EntityFrameworkCore.Analyzers` | 10.0.13 | MIT | Transitive | WebApp, API, Worker, MCP |
| `Microsoft.EntityFrameworkCore.Relational` | 10.0.13 | MIT | Transitive | WebApp, API, Worker, MCP |
| `Microsoft.EntityFrameworkCore.Sqlite` | 10.0.13 | MIT | Direct | WebApp |
| `Microsoft.EntityFrameworkCore.Sqlite.Core` | 10.0.13 | MIT | Transitive | WebApp |
| `Microsoft.EntityFrameworkCore.SqlServer` | 10.0.13 | MIT | Direct | WebApp, API, Worker, MCP |
| `Microsoft.Extensions.AI.Abstractions` | 10.5.2 | MIT | Transitive | MCP |
| `Microsoft.Extensions.ApiDescription.Server` | 10.0.0 | MIT | Transitive | API |
| `Microsoft.Extensions.Caching.Abstractions` | 10.0.13 | MIT | Transitive | Worker |
| `Microsoft.Extensions.Caching.Memory` | 10.0.13 | MIT | Transitive | Worker |
| `Microsoft.Extensions.Configuration` | 10.0.13 | MIT | Transitive | Worker |
| `Microsoft.Extensions.Configuration.Abstractions` | 10.0.13 | MIT | Transitive | Worker |
| `Microsoft.Extensions.Configuration.Binder` | 10.0.13 | MIT | Transitive | Worker |
| `Microsoft.Extensions.Configuration.CommandLine` | 10.0.13 | MIT | Transitive | Worker |
| `Microsoft.Extensions.Configuration.EnvironmentVariables` | 10.0.13 | MIT | Transitive | Worker |
| `Microsoft.Extensions.Configuration.FileExtensions` | 10.0.13 | MIT | Transitive | Worker |
| `Microsoft.Extensions.Configuration.Json` | 10.0.13 | MIT | Transitive | Worker |
| `Microsoft.Extensions.Configuration.UserSecrets` | 10.0.13 | MIT | Transitive | Worker |
| `Microsoft.Extensions.DependencyInjection` | 10.0.13 | MIT | Transitive | Worker |
| `Microsoft.Extensions.DependencyInjection.Abstractions` | 10.0.13 | MIT | Transitive | Worker |
| `Microsoft.Extensions.DependencyModel` | 10.0.0 | MIT | Transitive | API, Worker |
| `Microsoft.Extensions.DependencyModel` | 10.0.13 | MIT | Transitive | WebApp |
| `Microsoft.Extensions.Diagnostics` | 10.0.13 | MIT | Transitive | Worker |
| `Microsoft.Extensions.Diagnostics.Abstractions` | 10.0.13 | MIT | Transitive | Worker |
| `Microsoft.Extensions.FileProviders.Abstractions` | 10.0.13 | MIT | Transitive | Worker |
| `Microsoft.Extensions.FileProviders.Embedded` | 10.0.13 | MIT | Transitive | WebApp |
| `Microsoft.Extensions.FileProviders.Physical` | 10.0.13 | MIT | Transitive | Worker |
| `Microsoft.Extensions.FileSystemGlobbing` | 10.0.13 | MIT | Transitive | Worker |
| `Microsoft.Extensions.Hosting` | 10.0.13 | MIT | Direct | Worker |
| `Microsoft.Extensions.Hosting.Abstractions` | 10.0.13 | MIT | Transitive | Worker |
| `Microsoft.Extensions.Hosting.WindowsServices` | 10.0.13 | MIT | Direct | Worker |
| `Microsoft.Extensions.Identity.Core` | 10.0.13 | MIT | Transitive | Worker |
| `Microsoft.Extensions.Identity.Stores` | 10.0.13 | MIT | Transitive | Worker |
| `Microsoft.Extensions.Logging` | 10.0.13 | MIT | Transitive | Worker |
| `Microsoft.Extensions.Logging.Abstractions` | 10.0.13 | MIT | Transitive | Worker |
| `Microsoft.Extensions.Logging.Configuration` | 10.0.13 | MIT | Transitive | Worker |
| `Microsoft.Extensions.Logging.Console` | 10.0.13 | MIT | Transitive | Worker |
| `Microsoft.Extensions.Logging.Debug` | 10.0.13 | MIT | Transitive | Worker |
| `Microsoft.Extensions.Logging.EventLog` | 10.0.13 | MIT | Transitive | Worker |
| `Microsoft.Extensions.Logging.EventSource` | 10.0.13 | MIT | Transitive | Worker |
| `Microsoft.Extensions.Options` | 10.0.13 | MIT | Transitive | Worker |
| `Microsoft.Extensions.Options.ConfigurationExtensions` | 10.0.13 | MIT | Transitive | Worker |
| `Microsoft.Extensions.Primitives` | 10.0.13 | MIT | Transitive | Worker |
| `Microsoft.Identity.Client` | 4.84.2 | MIT | Transitive | WebApp, API, Worker, MCP |
| `Microsoft.Identity.Client.Broker` | 4.84.2 | MIT | Transitive | WebApp, API, Worker, MCP |
| `Microsoft.Identity.Client.Extensions.Msal` | 4.78.0 | MIT | Transitive | WebApp, API, Worker, MCP |
| `Microsoft.Identity.Client.NativeInterop` | 0.20.6 | Microsoft Software License Terms (bundled `LICENSE`) | Transitive | WebApp, API, Worker, MCP |
| `Microsoft.IdentityModel.Abstractions` | 8.14.0 | MIT | Transitive | MCP |
| `Microsoft.IdentityModel.Abstractions` | 8.16.0 | MIT | Transitive | WebApp, API, Worker |
| `Microsoft.IdentityModel.JsonWebTokens` | 7.7.1 | MIT | Transitive | MCP |
| `Microsoft.IdentityModel.JsonWebTokens` | 8.16.0 | MIT | Transitive | WebApp, API, Worker |
| `Microsoft.IdentityModel.Logging` | 7.7.1 | MIT | Transitive | MCP |
| `Microsoft.IdentityModel.Logging` | 8.16.0 | MIT | Transitive | WebApp, API, Worker |
| `Microsoft.IdentityModel.Protocols` | 7.7.1 | MIT | Transitive | MCP |
| `Microsoft.IdentityModel.Protocols` | 8.16.0 | MIT | Transitive | WebApp, API, Worker |
| `Microsoft.IdentityModel.Protocols.OpenIdConnect` | 7.7.1 | MIT | Transitive | MCP |
| `Microsoft.IdentityModel.Protocols.OpenIdConnect` | 8.16.0 | MIT | Transitive | WebApp, API, Worker |
| `Microsoft.IdentityModel.Tokens` | 7.7.1 | MIT | Transitive | MCP |
| `Microsoft.IdentityModel.Tokens` | 8.16.0 | MIT | Transitive | WebApp, API, Worker |
| `Microsoft.OpenApi` | 2.12.2 | MIT | Transitive | API |
| `Microsoft.SqlServer.Server` | 1.0.0 | MIT | Transitive | WebApp, API, Worker, MCP |
| `Microsoft.VisualStudio.Azure.Containers.Tools.Targets` | 1.23.0 | Microsoft Software License Terms (bundled `EULA.md`) | Direct | WebApp, API, Worker |
| `ModelContextProtocol` | 1.4.1 | Apache-2.0 | Transitive | MCP |
| `ModelContextProtocol.AspNetCore` | 1.4.1 | Apache-2.0 | Direct | MCP |
| `ModelContextProtocol.Core` | 1.4.1 | Apache-2.0 | Transitive | MCP |
| `Newtonsoft.Json` | 13.0.4 | MIT | Transitive | WebApp, API, Worker, MCP |
| `Npgsql` | 10.0.3 | PostgreSQL | Transitive | WebApp, API, Worker, MCP |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | 10.0.3 | PostgreSQL | Direct | WebApp, API, Worker, MCP |
| `RBush.Signed` | 4.0.0 | MIT | Transitive | WebApp |
| `Serilog` | 4.3.1 | Apache-2.0 | Transitive | WebApp, API, Worker |
| `Serilog` | 4.4.0 | Apache-2.0 | Direct | Console |
| `Serilog.AspNetCore` | 10.0.0 | Apache-2.0 | Direct | WebApp, API |
| `Serilog.Enrichers.Environment` | 3.0.1 | Apache-2.0 | Direct | WebApp, API, Worker |
| `Serilog.Extensions.Hosting` | 10.0.0 | Apache-2.0 | Direct | WebApp, API, Worker |
| `Serilog.Extensions.Logging` | 10.0.0 | Apache-2.0 | Transitive | WebApp, API, Worker |
| `Serilog.Formatting.Compact` | 3.0.0 | Apache-2.0 | Transitive | WebApp, API |
| `Serilog.Settings.Configuration` | 10.0.1 | Apache-2.0 | Direct | WebApp, API, Worker |
| `Serilog.Sinks.Console` | 6.1.1 | Apache-2.0 | Direct | WebApp, API, Worker, Console |
| `Serilog.Sinks.Debug` | 3.0.0 | Apache-2.0 | Transitive | WebApp, API |
| `Serilog.Sinks.File` | 7.0.0 | Apache-2.0 | Direct | WebApp, API, Console |
| `Serilog.Sinks.MSSqlServer` | 10.0.0 | Apache-2.0 | Direct | WebApp, API, Worker |
| `SixLabors.Fonts` | 1.0.0 | Apache-2.0 | Transitive | WebApp |
| `SQLitePCLRaw.bundle_e_sqlite3` | 2.1.13 | Apache-2.0 | Transitive | WebApp |
| `SQLitePCLRaw.core` | 2.1.13 | Apache-2.0 | Transitive | WebApp |
| `SQLitePCLRaw.lib.e_sqlite3` | 2.1.13 | Apache-2.0 | Transitive | WebApp |
| `SQLitePCLRaw.provider.e_sqlite3` | 2.1.13 | Apache-2.0 | Transitive | WebApp |
| `Swashbuckle.AspNetCore` | 10.3.0 | MIT | Direct | API |
| `Swashbuckle.AspNetCore.Swagger` | 10.3.0 | MIT | Transitive | API |
| `Swashbuckle.AspNetCore.SwaggerGen` | 10.3.0 | MIT | Transitive | API |
| `Swashbuckle.AspNetCore.SwaggerUI` | 10.3.0 | MIT | Transitive | API |
| `System.ClientModel` | 1.8.0 | MIT | Transitive | MCP |
| `System.ClientModel` | 1.9.0 | MIT | Transitive | WebApp, API, Worker |
| `System.Configuration.ConfigurationManager` | 9.0.11 | MIT | Transitive | MCP |
| `System.Configuration.ConfigurationManager` | 9.0.21 | MIT | Transitive | WebApp, API, Worker |
| `System.Diagnostics.EventLog` | 10.0.13 | MIT | Transitive | Worker |
| `System.IdentityModel.Tokens.Jwt` | 7.7.1 | MIT | Transitive | MCP |
| `System.IdentityModel.Tokens.Jwt` | 8.16.0 | MIT | Transitive | WebApp, API, Worker |
| `System.IO.Packaging` | 8.0.1 | MIT | Transitive | WebApp |
| `System.Memory.Data` | 8.0.1 | MIT | Transitive | MCP |
| `System.Memory.Data` | 10.0.1 | MIT | Transitive | WebApp, API, Worker |
| `System.Security.Cryptography.Pkcs` | 10.0.13 | MIT | Direct | WebApp, API, Worker, MCP |
| `System.Security.Cryptography.ProtectedData` | 9.0.11 | MIT | Transitive | MCP |
| `System.Security.Cryptography.ProtectedData` | 9.0.21 | MIT | Transitive | WebApp, API, Worker |
| `System.ServiceProcess.ServiceController` | 10.0.13 | MIT | Transitive | Worker |
| `System.Threading.RateLimiting` | 9.0.21 | MIT | Transitive | Worker |

## 7. Known vulnerabilities

Checked on 2026-11-14 against the NuGet.org vulnerability database (GitHub Advisory Database data) with:

```bash
dotnet list GxPAuditTrailReview.slnx package --vulnerable --include-transitive
dotnet list GxPAuditTrailReview.slnx package --deprecated --include-transitive
```

### 7.1 Vulnerable packages

**None.** `dotnet list package --vulnerable --include-transitive` reports no vulnerable package for any project of the solution, including the test projects.

### 7.2 Deprecated packages

No shipped component references a deprecated package. The only hit is `MSTest` 4.0.2 (marked *Legacy*), a direct reference of `AuditTrailReview.UIPlaywrightTest`, a test project that is not shipped.

### 7.3 Limits of this check

This reflects advisories known to NuGet.org on 2026-11-14; new advisories are published continuously. It does not cover the .NET runtime / ASP.NET Core shared framework, the Docker base image OS layers, SQL Server, PostgreSQL or the bundled client-side libraries. Re-run the check on the release commit before distribution.

## 8. Packages used only for build and test (not shipped)

Pulled in by `AuditTrailReview.UnitTest`, `AuditTrailReview.UISeleniumTest` and `AuditTrailReview.UIPlaywrightTest`; not part of any deployable. Only versions not already listed in section 6 are shown.

| Package | Version | License (SPDX / terms) | Type |
|---|---|---|---|
| `coverlet.collector` | 10.1.0 | MIT | Direct |
| `Microsoft.ApplicationInsights` | 2.23.0 | MIT | Transitive |
| `Microsoft.Bcl.AsyncInterfaces` | 6.0.0 | MIT | Transitive |
| `Microsoft.CodeCoverage` | 18.0.1 | MIT | Transitive |
| `Microsoft.CodeCoverage` | 18.10.1 | MIT | Transitive |
| `Microsoft.CodeCoverage` | 18.11.0 | MIT | Transitive |
| `Microsoft.DiaSymReader` | 2.0.0 | MIT | Transitive |
| `Microsoft.DiaSymReader` | 2.2.10 | MIT | Transitive |
| `Microsoft.EntityFrameworkCore.InMemory` | 10.0.13 | MIT | Direct |
| `Microsoft.Extensions.DependencyModel` | 10.0.10 | MIT | Transitive |
| `Microsoft.Extensions.DependencyModel` | 6.0.2 | MIT | Transitive |
| `Microsoft.Extensions.Hosting.Abstractions` | 10.0.7 | MIT | Transitive |
| `Microsoft.NET.Test.Sdk` | 18.0.1 | MIT | Transitive |
| `Microsoft.NET.Test.Sdk` | 18.10.1 | MIT | Transitive |
| `Microsoft.NET.Test.Sdk` | 18.11.0 | MIT | Direct |
| `Microsoft.Playwright` | 1.64.1 | MIT | Transitive |
| `Microsoft.Playwright.MSTest` | 1.64.1 | MIT | Direct |
| `Microsoft.Playwright.TestAdapter` | 1.64.1 | MIT | Transitive |
| `Microsoft.Testing.Extensions.CodeCoverage` | 18.1.0 | Microsoft Software License Terms (bundled `License.txt`) | Transitive |
| `Microsoft.Testing.Extensions.CodeCoverage` | 18.12.0 | Microsoft Software License Terms (bundled `License.txt`) | Transitive |
| `Microsoft.Testing.Extensions.Telemetry` | 2.0.2 | MIT | Transitive |
| `Microsoft.Testing.Extensions.Telemetry` | 2.5.1 | MIT | Transitive |
| `Microsoft.Testing.Extensions.TrxReport` | 2.0.2 | MIT | Transitive |
| `Microsoft.Testing.Extensions.TrxReport` | 2.5.1 | MIT | Transitive |
| `Microsoft.Testing.Extensions.TrxReport.Abstractions` | 2.0.2 | MIT | Transitive |
| `Microsoft.Testing.Extensions.TrxReport.Abstractions` | 2.5.1 | MIT | Transitive |
| `Microsoft.Testing.Extensions.VSTestBridge` | 2.0.2 | MIT | Transitive |
| `Microsoft.Testing.Platform` | 2.0.2 | MIT | Transitive |
| `Microsoft.Testing.Platform` | 2.5.1 | MIT | Transitive |
| `Microsoft.Testing.Platform.MSBuild` | 2.0.2 | MIT | Transitive |
| `Microsoft.Testing.Platform.MSBuild` | 2.5.1 | MIT | Transitive |
| `Microsoft.TestPlatform.AdapterUtilities` | 18.0.1 | MIT | Transitive |
| `Microsoft.TestPlatform.ObjectModel` | 18.0.1 | MIT | Transitive |
| `Microsoft.TestPlatform.ObjectModel` | 18.10.1 | MIT | Transitive |
| `Microsoft.TestPlatform.ObjectModel` | 18.11.0 | MIT | Transitive |
| `Microsoft.TestPlatform.TestHost` | 18.0.1 | MIT | Transitive |
| `Microsoft.TestPlatform.TestHost` | 18.10.1 | MIT | Transitive |
| `Microsoft.TestPlatform.TestHost` | 18.11.0 | MIT | Transitive |
| `Mono.Cecil` | 0.11.6 | MIT | Transitive |
| `MSTest` | 4.0.2 | MIT | Direct |
| `MSTest` | 4.5.1 | MIT | Direct |
| `MSTest.Analyzers` | 4.0.2 | MIT | Transitive |
| `MSTest.Analyzers` | 4.5.1 | MIT | Transitive |
| `MSTest.TestAdapter` | 4.0.2 | MIT | Transitive |
| `MSTest.TestAdapter` | 4.5.1 | MIT | Direct |
| `MSTest.TestFramework` | 4.0.2 | MIT | Transitive |
| `MSTest.TestFramework` | 4.5.1 | MIT | Direct |
| `Newtonsoft.Json` | 13.0.3 | MIT | Transitive |
| `Selenium.Support` | 4.51.0 | Apache-2.0 | Direct |
| `Selenium.WebDriver` | 4.51.0 | Apache-2.0 | Direct |
| `System.ComponentModel.Annotations` | 5.0.0 | MIT | Transitive |
| `System.Diagnostics.EventLog` | 9.0.21 | MIT | Transitive |

## 9. Observations

- **Multiple versions of one package.** Within the shipped components these resolve to more than one version: `Azure.Core` (1.50.0 / 1.51.1), `Azure.Identity` (1.17.1 / 1.18.0), `Microsoft.Bcl.AsyncInterfaces` (8.0.0 / 10.0.2), `Microsoft.Data.SqlClient` (6.1.7 / 7.1.2), `Microsoft.Data.SqlClient.SNI.runtime` (6.0.3 / 7.1.2), `Microsoft.Extensions.DependencyModel` (10.0.0 / 10.0.13), `Microsoft.IdentityModel.Abstractions` (8.14.0 / 8.16.0), `Microsoft.IdentityModel.JsonWebTokens` (7.7.1 / 8.16.0), `Microsoft.IdentityModel.Logging` (7.7.1 / 8.16.0), `Microsoft.IdentityModel.Protocols` (7.7.1 / 8.16.0), `Microsoft.IdentityModel.Protocols.OpenIdConnect` (7.7.1 / 8.16.0), `Microsoft.IdentityModel.Tokens` (7.7.1 / 8.16.0), `Serilog` (4.3.1 / 4.4.0), `System.ClientModel` (1.8.0 / 1.9.0), `System.Configuration.ConfigurationManager` (9.0.11 / 9.0.21), `System.IdentityModel.Tokens.Jwt` (7.7.1 / 8.16.0), `System.Memory.Data` (8.0.1 / 10.0.1), `System.Security.Cryptography.ProtectedData` (9.0.11 / 9.0.21). This results from transitive resolution; each version is listed above.
- **The MCP server resolves older transitive versions** of `Microsoft.Data.SqlClient` (6.1.7) and `Microsoft.IdentityModel.*` (7.7.1) than the other components, because it does not reference `Serilog.Sinks.MSSqlServer`. No advisory applies to them (section 7).
- **New since v0.8:** `ModelContextProtocol`, `ModelContextProtocol.Core` and `ModelContextProtocol.AspNetCore` 1.4.1 (Apache-2.0, MCP server).
- **Native binaries.** `Microsoft.Data.SqlClient.SNI.runtime`, `Microsoft.Identity.Client.NativeInterop` and `SQLitePCLRaw.lib.e_sqlite3` carry native components.
- **Not a substitute for the build.** This SBOM records resolved versions; it does not replace the CI build and test run.

## 10. Regenerating this document

From the repository root, with a .NET 10 SDK:

```bash
dotnet restore GxPAuditTrailReview.slnx
dotnet list AuditTrailReview.WebApp/AuditTrailReview.WebApp.csproj package --include-transitive --format json
# repeat for IntegrationHub.API, IntegrationHub.WorkerService, Console and McpServer
dotnet list GxPAuditTrailReview.slnx package --vulnerable --include-transitive
dotnet list GxPAuditTrailReview.slnx package --deprecated --include-transitive
```

License identifiers: read the `<license>` element of each package's `.nuspec` in the NuGet cache (`~/.nuget/packages/<id>/<version>`).
