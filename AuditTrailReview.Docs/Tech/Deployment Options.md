# Deployment Options

Start here to decide how to run GxPAuditTrailReview. The Installation Manual is the core, validated procedure. The other runbooks cover one platform each and link back here for the facts they share.

## Which deployment do I choose?

| Target | Database | Files | Intended use | Runbook |
|---|---|---|---|---|
| Windows or Linux host, manual install | SQL Server (LocalDB or full) | SQL scripts in `AuditTrailReview.Database/Database schema/` | **Supported deployment.** The only path covered by the Validation Package | [Installation Manual](Installation%20Manual.md) |
| Azure (Container Apps) | Azure SQL (SQL scripts applied by the pipeline, managed identity, no SQL password) | `pipelines/pipeline-deploy-on-azure-cloud.yml`, `deploy/infra`, `deploy/README.md` | Customer environment on the SQL Server schema. Validation status: TBD, owner to decide | [Azure Pipelines](../CICD/Azure%20Pipelines.md), `deploy/README.md` |
| Docker on a VPS | PostgreSQL 16 (`docker/docker-compose.postgres.yml`) or SQLite (`docker/docker-compose.sqlite.yml`) | Compose files in `docker/`, run from the repository root | **Evaluation, demos, functional testing only.** Not for real GxP use (ADR-0009) | [Run docker compose on VPS](../CICD/Run%20docker%20compose%20on%20VPS.md) |
| Docker on a developer machine | PostgreSQL or SQLite | Same compose files | Local evaluation and development | [Run docker compose locally](../CICD/Run%20docker%20compose%20locally.md) |

Two points that are easy to miss:

- The **Azure** path does not use PostgreSQL. It runs the same SQL Server schema as the Installation Manual, so it is the one to extend if you need a hosted, production-style deployment.
- **Docker Compose with PostgreSQL** builds its schema from the EF model, not from the SQL scripts. There is no upgrade path between versions, so treat its data as disposable.

## Facts that apply to every deployment

These are stated once here. Runbooks should link to this section rather than repeat it.

1. **Four components, one database.** WebApp, Integration Hub API, Worker Service and the Console client. All server components share one database (ADR-0002).
2. **A Worker Service must run wherever the API runs.** Every API upload becomes a queued job; without a worker, jobs stay `Queued`. The SQLite flavor has no worker, so uploads that need the queue also stay `Queued`.
3. **Schema is raw SQL scripts, not EF migrations**, on the supported path (ADR-0004). Install the snapshot, then the incremental scripts in version order (`0.7` sorts after `0.20` alphabetically, so sort by version number). The Docker flavors are the exception (see above).
4. **First admin account.** It is created once from `SeedAccount__Email` / `SeedAccount__Password` against an empty user table. Change the placeholder values before exposing any server.
5. **One shared encryption key.** `Security__EncryptionKey` must be identical on the WebApp, API, Worker and MCP server. It encrypts stored secrets and the generated API keys; if it is empty, API and MCP requests are rejected with `401`.
6. **Large uploads need memory.** See *Hardware sizing* in the Installation Manual and *Large-file processing* in the Technical Manual.
7. **Timestamps are stored as UTC.**

## Related documents

- [Installation Manual](Installation%20Manual.md) and [Technical Manual](Technical%20Manual.md): the core documents.
- ADR-0002 (shared database), ADR-0004 (raw SQL schema), ADR-0009 (Docker evaluation flavors) in `AuditTrailReview.Docs/ADR/`.
