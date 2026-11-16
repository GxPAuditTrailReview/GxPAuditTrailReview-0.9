# Software Bill of Materials (SBOM) – GxPAuditTrail v0.8

| | |
|---|---|
| **Product** | GxPAuditTrail – Audit Trail Review |
| **Release** | v0.8 |
| **Source revision** | `0687332` (branch `master`, after the library updates) |
| **SBOM generated** | 2026-10-15 |
| **Product license** | Business Source License 1.1 (see `LICENSE` at the repository root; Licensor: Axteon sp. z o.o.) |
| **Format** | Human-readable Markdown inventory (not SPDX / CycloneDX) |
| **Target framework** | .NET 10 (`net10.0`); SDK pinned in `global.json` (10.0.400, `rollForward: patch`) |

## 1. Scope and method

This document lists the third-party software that is built into or distributed with the four
deployable components of v0.8:

| Component | Project | Deployment form |
|---|---|---|
| **WebApp** | `AuditTrailReview.WebApp` (Razor Pages + Identity) | Web application, Docker image |
| **API** | `AuditTrailReview.IntegrationHub.API` | HTTP API, Docker image |
| **Worker** | `AuditTrailReview.IntegrationHub.WorkerService` | Windows Service (Inno Setup installer in `installer/WorkerService/`), Docker image |
| **Console** | `AuditTrailReview.Console` | Command-line client |

`AuditTrailReview.Engine`, `AuditTrailReview.Database` and `AuditTrailReview.Model` are first-party
libraries referenced by the components above; their NuGet dependencies are included in each
component's closure. `AuditTrailReview.Docs` has no package dependencies.

The NuGet inventory was produced from a restored solution (`dotnet restore GxPAuditTrail.slnx`) with
.NET SDK 10.0.401, using, per project:

```bash
dotnet list <project>.csproj package --include-transitive --format json
```

Versions are the **resolved** versions. License identifiers come from each package's `.nuspec`
`<license>` element. Where a package declares a license file instead of an SPDX expression, the file is
named in the table; these are Microsoft license terms that permit use and redistribution of the binaries
within an application.

## 2. Summary

| Item | Count |
|---|---|
| Distinct runtime NuGet package versions (all four components) | 104 |
| Of which direct package references | 27 |
| Build/test-only package versions (not shipped) | 28 |
| Bundled client-side libraries (WebApp) | 4 |
| Known vulnerabilities in shipped packages (section 7) | 0 |
| Deprecated packages (section 7) | 0 |

License distribution of the runtime NuGet packages:

| License | Packages |
|---|---|
| MIT | 81 |
| Apache-2.0 | 17 |
| PostgreSQL | 2 |
| MS-PL OR Apache-2.0 | 1 |
| BSD-2-Clause | 1 |
| Microsoft Software License Terms (bundled `EULA.md`; design-time Docker tooling) | 1 |
| Microsoft Software License Terms (bundled `LICENSE.txt`) | 1 |

No GPL / LGPL / AGPL (copyleft) licenses are present in the runtime closure.

## 3. Platform and runtime

| Item | Version | License | Notes |
|---|---|---|---|
| .NET runtime / ASP.NET Core shared framework | 10.0.x | MIT | Provided by the host, or by the Docker base image `mcr.microsoft.com/dotnet/aspnet:10.0` |
| .NET SDK (build only) | 10.0.400 (`global.json`) | MIT | Build image `mcr.microsoft.com/dotnet/sdk:10.0`; not shipped |
| Docker base images | `mcr.microsoft.com/dotnet/aspnet:10.0`, `mcr.microsoft.com/dotnet/sdk:10.0` | MIT (.NET); OS layers carry their own licenses | Used by the WebApp, API and Worker Dockerfiles. Image digests are not pinned |
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
| OpenAI API (`api.openai.com`: chat completions, embeddings) | WebApp / Engine (`Engine/AI/OpenAIService.cs`) | AI-supported audit trail analysis; "Ask the docs" chatbot (models `gpt-4`, `gpt-4o-mini`, `text-embedding-3-small`) | Plain HTTPS calls, no vendor SDK. Needs a customer-supplied API key; a failing AI step never fails a scan |
| Microsoft SQL Server (incl. LocalDB) | All server components | Primary supported database | Customer-provided |
| PostgreSQL | Docker flavor (`docker-compose.postgres.yml`) | Trial / functional-testing database | Not for production; via `Npgsql` |
| SQLite | Docker flavor (`docker-compose.sqlite.yml`) | Trial / functional-testing database | Not for production; native library from `SQLitePCLRaw.lib.e_sqlite3` |
| FTP server | Worker | Audit trail file drop folder | Customer-provided; client library `FluentFTP` |

## 6. NuGet packages shipped with the components

"Direct" means a `PackageReference` in a shipped project (including `Engine` and `Database`);
"Transitive" means pulled in by another package.

| Package | Version | License (SPDX / terms) | Type | Shipped in |
|---|---|---|---|---|
| `bootstrap` | 5.3.8 | MIT | Direct | WebApp |
| `ClosedXML` | 0.105.1 | MIT | Direct | WebApp |
| `ClosedXML.Parser` | 2.0.0 | MIT | Transitive | WebApp |
| `CsvHelper` | 33.1.0 | MS-PL OR Apache-2.0 | Direct | WebApp, API, Worker |
| `DocumentFormat.OpenXml` | 3.1.1 | MIT | Transitive | WebApp |
| `DocumentFormat.OpenXml.Framework` | 3.1.1 | MIT | Transitive | WebApp |
| `ExcelNumberFormat` | 1.1.0 | MIT | Transitive | WebApp |
| `FluentFTP` | 55.0.0 | MIT | Direct | Worker |
| `Markdig` | 1.4.0 | BSD-2-Clause | Direct | WebApp, API, Worker |
| `Microsoft.AspNetCore.Cryptography.Internal` | 10.0.13 | MIT | Transitive | Worker |
| `Microsoft.AspNetCore.Cryptography.KeyDerivation` | 10.0.13 | MIT | Transitive | Worker |
| `Microsoft.AspNetCore.Identity.EntityFrameworkCore` | 10.0.13 | MIT | Direct | WebApp, API, Worker |
| `Microsoft.AspNetCore.Identity.UI` | 10.0.13 | MIT | Direct | WebApp |
| `Microsoft.AspNetCore.OpenApi` | 10.0.13 | MIT | Direct | API |
| `Microsoft.Bcl.Cryptography` | 9.0.13 | MIT | Transitive | WebApp, API, Worker |
| `Microsoft.Data.SqlClient` | 7.0.1 | MIT | Transitive | WebApp, API, Worker |
| `Microsoft.Data.SqlClient.Extensions.Abstractions` | 1.0.0 | MIT | Transitive | WebApp, API, Worker |
| `Microsoft.Data.SqlClient.Internal.Logging` | 1.0.0 | MIT | Transitive | WebApp, API, Worker |
| `Microsoft.Data.SqlClient.SNI.runtime` | 6.0.2 | Microsoft Software License Terms (bundled `LICENSE.txt`) | Transitive | WebApp, API, Worker |
| `Microsoft.Data.Sqlite.Core` | 10.0.13 | MIT | Transitive | WebApp |
| `Microsoft.EntityFrameworkCore` | 10.0.13 | MIT | Direct | WebApp, API, Worker |
| `Microsoft.EntityFrameworkCore.Abstractions` | 10.0.13 | MIT | Transitive | WebApp, API, Worker |
| `Microsoft.EntityFrameworkCore.Analyzers` | 10.0.13 | MIT | Transitive | WebApp, API, Worker |
| `Microsoft.EntityFrameworkCore.Relational` | 10.0.13 | MIT | Transitive | WebApp, API, Worker |
| `Microsoft.EntityFrameworkCore.Sqlite` | 10.0.13 | MIT | Direct | WebApp |
| `Microsoft.EntityFrameworkCore.Sqlite.Core` | 10.0.13 | MIT | Transitive | WebApp |
| `Microsoft.EntityFrameworkCore.SqlServer` | 10.0.13 | MIT | Direct | WebApp, API, Worker |
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
| `Microsoft.IdentityModel.Abstractions` | 8.16.0 | MIT | Transitive | WebApp, API, Worker |
| `Microsoft.IdentityModel.JsonWebTokens` | 8.16.0 | MIT | Transitive | WebApp, API, Worker |
| `Microsoft.IdentityModel.Logging` | 8.16.0 | MIT | Transitive | WebApp, API, Worker |
| `Microsoft.IdentityModel.Protocols` | 8.16.0 | MIT | Transitive | WebApp, API, Worker |
| `Microsoft.IdentityModel.Protocols.OpenIdConnect` | 8.16.0 | MIT | Transitive | WebApp, API, Worker |
| `Microsoft.IdentityModel.Tokens` | 8.16.0 | MIT | Transitive | WebApp, API, Worker |
| `Microsoft.OpenApi` | 2.12.2 | MIT | Transitive | API |
| `Microsoft.SqlServer.Server` | 1.0.0 | MIT | Transitive | WebApp, API, Worker |
| `Microsoft.VisualStudio.Azure.Containers.Tools.Targets` | 1.23.0 | Microsoft Software License Terms (bundled `EULA.md`; design-time Docker tooling) | Direct | WebApp, API, Worker |
| `Newtonsoft.Json` | 13.0.4 | MIT | Direct | WebApp, API, Worker |
| `Npgsql` | 10.0.3 | PostgreSQL | Transitive | WebApp, API, Worker |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | 10.0.3 | PostgreSQL | Direct | WebApp, API, Worker |
| `RBush.Signed` | 4.0.0 | MIT | Transitive | WebApp |
| `Serilog` | 4.3.1 | Apache-2.0 | Direct | WebApp, API, Worker |
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
| `System.Configuration.ConfigurationManager` | 9.0.15 | MIT | Transitive | WebApp, API, Worker |
| `System.Diagnostics.EventLog` | 10.0.13 | MIT | Transitive | Worker |
| `System.IdentityModel.Tokens.Jwt` | 8.16.0 | MIT | Transitive | WebApp, API, Worker |
| `System.IO.Packaging` | 8.0.1 | MIT | Transitive | WebApp |
| `System.Security.Cryptography.Pkcs` | 10.0.13 | MIT | Direct | WebApp, API, Worker |
| `System.Security.Cryptography.ProtectedData` | 9.0.15 | MIT | Transitive | WebApp, API, Worker |
| `System.ServiceProcess.ServiceController` | 10.0.13 | MIT | Transitive | Worker |

## 7. Known vulnerabilities

Re-checked on 2026-10-15, after the library updates, against the NuGet.org vulnerability database (GitHub Advisory Database data) with:

```bash
dotnet list GxPAuditTrail.slnx package --vulnerable --include-transitive
dotnet list GxPAuditTrail.slnx package --deprecated --include-transitive
```

### 7.1 Vulnerable packages in the shipped components

**None.** `dotnet list package --vulnerable --include-transitive` reports no vulnerable package for any
project of the solution (Console, Database, Docs, Engine, IntegrationHub.API, IntegrationHub.WorkerService,
Model, UnitTest, UISeleniumTest, WebApp).

Resolved since the first v0.8 SBOM draft: `System.Security.Cryptography.Pkcs` 9.0.11 (transitive via
`Microsoft.Data.SqlClient`) was affected by two denial-of-service advisories,
[GHSA-846f-f764-76mx](https://github.com/advisories/GHSA-846f-f764-76mx) / CVE-2026-84008 (High, CVSS 7.5) and
[GHSA-2fmv-rv2c-59x7](https://github.com/advisories/GHSA-2fmv-rv2c-59x7) / CVE-2026-70162 (Moderate, CVSS 5.9),
both fixed in 8.0.2 / 9.0.21 / 10.0.13. The solution now references `System.Security.Cryptography.Pkcs`
**10.0.13** explicitly in the Database, Engine, API and WorkerService projects (commit `0687332`; the WebApp
gets it through Engine), so every component resolves the patched version.

### 7.2 Deprecated packages

`dotnet list package --deprecated --include-transitive` reports no deprecated package in any project.

### 7.3 Limits of this check

This reflects advisories known to NuGet.org at the time of the check (2026-10-15); new advisories are
published continuously. It does not cover the .NET runtime / ASP.NET Core shared framework, the Docker base
image OS layers, SQL Server, PostgreSQL or the bundled client-side libraries. Re-run the check before every
distribution.

## 8. Packages used only for build and test (not shipped)

Pulled in by `AuditTrailReview.UnitTest` and `AuditTrailReview.UISeleniumTest`; not part of any
deployable. Only versions not already listed in section 6 are shown.

| Package | Version | License (SPDX / terms) | Type |
|---|---|---|---|
| `coverlet.collector` | 10.1.0 | MIT | Transitive |
| `Microsoft.ApplicationInsights` | 2.23.0 | MIT | Transitive |
| `Microsoft.CodeCoverage` | 18.10.1 | MIT | Transitive |
| `Microsoft.CodeCoverage` | 18.11.0 | MIT | Transitive |
| `Microsoft.DiaSymReader` | 2.2.10 | MIT | Transitive |
| `Microsoft.EntityFrameworkCore.InMemory` | 10.0.13 | MIT | Transitive |
| `Microsoft.Extensions.DependencyModel` | 10.0.10 | MIT | Transitive |
| `Microsoft.Extensions.Hosting.Abstractions` | 10.0.0 | MIT | Transitive |
| `Microsoft.NET.Test.Sdk` | 18.10.1 | MIT | Transitive |
| `Microsoft.NET.Test.Sdk` | 18.11.0 | MIT | Transitive |
| `Microsoft.Testing.Extensions.CodeCoverage` | 18.12.0 | Microsoft Software License Terms (bundled `License.txt`) | Transitive |
| `Microsoft.Testing.Extensions.Telemetry` | 2.5.1 | MIT | Transitive |
| `Microsoft.Testing.Extensions.TrxReport` | 2.5.1 | MIT | Transitive |
| `Microsoft.Testing.Extensions.TrxReport.Abstractions` | 2.5.1 | MIT | Transitive |
| `Microsoft.Testing.Platform` | 2.5.1 | MIT | Transitive |
| `Microsoft.Testing.Platform.MSBuild` | 2.5.1 | MIT | Transitive |
| `Microsoft.TestPlatform.ObjectModel` | 18.10.1 | MIT | Transitive |
| `Microsoft.TestPlatform.ObjectModel` | 18.11.0 | MIT | Transitive |
| `Microsoft.TestPlatform.TestHost` | 18.10.1 | MIT | Transitive |
| `Microsoft.TestPlatform.TestHost` | 18.11.0 | MIT | Transitive |
| `Mono.Cecil` | 0.11.6 | MIT | Transitive |
| `MSTest` | 4.5.1 | MIT | Transitive |
| `MSTest.Analyzers` | 4.5.1 | MIT | Transitive |
| `MSTest.TestAdapter` | 4.5.1 | MIT | Transitive |
| `MSTest.TestFramework` | 4.5.1 | MIT | Transitive |
| `Selenium.Support` | 4.51.0 | Apache-2.0 | Transitive |
| `Selenium.WebDriver` | 4.51.0 | Apache-2.0 | Transitive |
| `System.Diagnostics.EventLog` | 9.0.15 | MIT | Transitive |

## 9. Observations

- **Multiple versions of one package.** Within the shipped components only `Serilog` (4.3.1 in API / Worker / WebApp, 4.4.0 in the Console) and
  `Microsoft.Extensions.DependencyModel` (10.0.0 / 10.0.13) resolve to more than one version. This results from transitive resolution; each version is
  listed above.
- **Library projects resolve older transitive versions.** `AuditTrailReview.Engine` and
  `AuditTrailReview.Database` resolve `Microsoft.Data.SqlClient` 6.1.7 (and `Microsoft.IdentityModel.*` 7.7.1)
  when built alone, while the deployable components resolve 7.0.1 (`Microsoft.IdentityModel.*` 8.16.0) through
  `Serilog.Sinks.MSSqlServer` 10.0.0. The deployable components are what is shipped, so section 6 lists the
  versions they resolve.
- **Mixed Microsoft package generations.** `System.Configuration.ConfigurationManager` and
  `System.Security.Cryptography.ProtectedData` (9.0.x) arrive transitively via `Microsoft.Data.SqlClient` /
  `Azure.Identity` and are not aligned with the 10.0.13 `Microsoft.Extensions.*` packages.
- **Native binaries.** `Microsoft.Data.SqlClient.SNI.runtime`, `Microsoft.Identity.Client.NativeInterop` and
  `SQLitePCLRaw.lib.e_sqlite3` carry native components.
- **Not build-verified here.** The dependency set includes major-version bumps (for example
  `Microsoft.Data.SqlClient` 7.x and `Serilog.AspNetCore` / `Serilog.Settings.Configuration` 10.x). This SBOM
  records the resolved versions; it does not replace the CI build and test run.

## 10. Regenerating this document

From the repository root, with a .NET 10 SDK:

```bash
dotnet restore GxPAuditTrail.slnx
dotnet list AuditTrailReview.WebApp/AuditTrailReview.WebApp.csproj package --include-transitive
dotnet list AuditTrailReview.IntegrationHub.API/AuditTrailReview.IntegrationHub.API.csproj package --include-transitive
dotnet list AuditTrailReview.IntegrationHub.WorkerService/AuditTrailReview.IntegrationHub.WorkerService.csproj package --include-transitive
dotnet list AuditTrailReview.Console/AuditTrailReview.Console.csproj package --include-transitive
dotnet list GxPAuditTrail.slnx package --vulnerable --include-transitive
dotnet list GxPAuditTrail.slnx package --deprecated --include-transitive
```

Update the revision, date and tables whenever a package reference, a bundled client-side library or a base
image changes for a release.
