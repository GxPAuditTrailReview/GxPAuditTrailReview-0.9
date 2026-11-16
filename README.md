# GxPAuditTrailReview

A web-based audit trail review platform for GxP-relevant systems. It lets teams upload audit
trail exports, standardize them into a common structure, apply configurable rule-based checks,
review findings, get a supplementary AI-assisted analysis, and close scans with sign-off — with
every review action itself captured in an internal audit trail.

## What's here

The solution is split into several .NET 10 projects:

- **AuditTrailReview.WebApp** — the Razor Pages web application (UI, ASP.NET Core Identity,
  EF Core). This is the primary way to use the product.
- **AuditTrailReview.Engine** — core business logic: file parsing, header-keyword mapping to a
  common model, the rule-based review engine (19 rules across 5 categories, each individually
  configurable and toggleable), OpenAI-based supplementary AI analysis, and shared services
  (email, activity logging, internal audit trail logging).
- **AuditTrailReview.Model** — shared domain model types.
- **AuditTrailReview.Database** — the EF Core `ApplicationDbContext`. Schema changes are managed
  via versioned, individually reviewable raw SQL scripts (`AuditTrailReview.Database/Database
  schema/`), not EF Core migrations.
- **AuditTrailReview.IntegrationHub.API** — an authenticated HTTP API for external/scripted scan
  submission.
- **AuditTrailReview.IntegrationHub.WorkerService** — a background service that polls an FTP
  location for new audit trail files and feeds them into the same processing pipeline as the
  API and the manual-upload path.
- **AuditTrailReview.Console** — a small cross-platform console client for posting a local file
  to the Integration Hub API, meant for scripted or scheduled submission.
- **AuditTrailReview.UnitTest** / **AuditTrailReview.UISeleniumTest** — automated test suites
  (MSTest, and Selenium WebDriver for UI flows).
- **AuditTrailReview.Docs** — requirements, architecture decision records, technical/installation
  manuals, and release notes for the project.

## Getting started

See `AuditTrailReview.Docs/Tech/Installation Manual.md` for a full step-by-step setup guide
(prerequisites, database creation, configuration, first run). In short: .NET 10 SDK, a SQL
Server/LocalDB instance, run the schema scripts under `AuditTrailReview.Database/Database
schema/` in order, point `AuditTrailReview.WebApp/appsettings.json` at your database, then
`dotnet run` (or open the solution in Visual Studio and run `AuditTrailReview.WebApp`).

An OpenAI API key is required for the manual-upload AI analysis step to succeed, and SMTP
credentials are only needed for outgoing account emails — both are configured the same way,
described in the installation manual. **Never commit real credentials** into a tracked
`appsettings.json`/`appsettings.Development.json`; `.gitignore` already excludes every
`appsettings.Development.json` in this repo for that reason — use `dotnet user-secrets` or
environment variables for local secrets instead.

## Documentation

`AuditTrailReview.Docs/` has the project's requirements, architecture decisions (`ADR/`),
technical and installation manuals (`Tech/`), and release notes (`ReleaseNotes/`).

## License

This project is licensed under the [Business Source License 1.1](LICENSE). It is source-available,
not open source: you may view, copy, modify, and use it non-productively, and it converts
automatically to the Apache License, Version 2.0 on the Change Date stated in the `LICENSE` file.
Production use before that date requires a commercial license from the Licensor. See the
`LICENSE` file for the exact terms, including the Additional Use Grant (if any).
