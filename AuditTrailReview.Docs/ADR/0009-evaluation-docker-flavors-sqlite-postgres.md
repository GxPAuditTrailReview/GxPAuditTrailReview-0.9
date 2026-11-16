# 0009. Self-contained SQLite and PostgreSQL Docker flavors for evaluation

Status: Accepted
Date: 2026-10-19 (implemented 2026-10-01 to 2026-10-02)

## Context
ADR-0002 and ADR-0004 make a SQL Server database, created from hand-written scripts, the supported deployment. That is a high bar for someone who only wants to try the product, run a demo, or exercise the Worker Service quickly. We wanted a one-command way to run the application without installing SQL Server, without weakening the supported path.

## Decision
Provide two Docker Compose flavors at the repository root, selected through `Database:Provider` (default `SqlServer`):

| File | Provider | Services | Notes |
|---|---|---|---|
| `docker/docker-compose.sqlite.yml` | `Sqlite` | WebApp only, database file on a volume | No Worker Service, so uploads that need the `Tier.Worker` queue stay `Queued` |
| `docker/docker-compose.postgres.yml` | `Postgres` | PostgreSQL 16, WebApp (8080), Integration Hub API (8081), Worker Service | The FTP path needs enabling under Settings > FTP and pointing at a reachable server |

Both create the schema from the EF Core model at startup (`EnsureCreatedAsync`) instead of running the SQL scripts, and the WebApp's reference-data seeding runs as usual. `appsettings.Sqlite.json` and `appsettings.Postgres.json` carry the provider and a connection string; the real password comes from the compose file via `ConnectionStrings__DefaultConnection`. `ScanUploadJobProcessor` has a provider-neutral second connection for streaming stored files on PostgreSQL. The flavors are for evaluation and functional testing only: not for real GxP use, and not a performance benchmark.

## Consequences
**Positive:**
- A working system in one command (`docker compose --project-directory . -f docker/docker-compose.postgres.yml up --build`), including the Worker Service and API.
- The same code paths run on three providers, which keeps provider-specific SQL honest.

**Negative / known trade-offs:**
- Two schema definitions now exist: the SQL scripts (SQL Server) and the EF model (SQLite and PostgreSQL). A change must be made in both, and nothing checks that they agree.
- Only the SQL Server path is covered by validation documents. Behavior that differs by provider (for example `ExecuteUpdateAsync` and the stored-file streaming) is only partly unit-tested; the SQL text for the file read is tested, the live stream is not.
- Compose files contain placeholder passwords that must be changed.
- Large files need a lot of memory in Docker; see `Technical Manual.md`, *Large-file processing*.

## Alternatives considered
- **A SQL Server container.** Rejected for the evaluation flavors: heavier, licence terms, and it would not remove the need to run the SQL scripts.
- **EF Core migrations for all providers.** Rejected for now (see ADR-0004): it would change the supported deployment story.
- **Documenting only the SQL Server install.** Rejected: too much friction for evaluation.
