# Azure Pipelines

This page describes the three Azure DevOps pipeline definitions stored in the `pipelines/` folder and listed under *Solution Items* in `GxPAuditTrailReview.slnx`: what each one does, when it runs, and how they relate. For the Docker Compose files in the same folder, see `Run docker compose locally.md` and `Run docker compose on VPS.md`. To choose between Azure, Docker and the manual install, see [Deployment Options](../Tech/Deployment%20Options.md).

| File | Purpose | Trigger | Output |
|---|---|---|---|
| `pipelines/pipeline-ci-build-unit-test-dev-ops.yml` | Continuous integration: build the solution and run the unit tests | Automatic, on every push to `master` | Build result and published test results |
| `pipelines/pipeline-ui-tests-external-env.yml` | UI tests: run the Playwright suite against an already running environment | Manual only (`trigger: none`, `pr: none`) | Published Playwright test results |
| `pipelines/pipeline-deploy-on-azure-cloud.yml` | Deployment: create or update a full customer environment in Azure | Manual only (`trigger: none`) | Running Container Apps, with the initial admin password in Key Vault |

The three are deliberately separate. CI stays fast and needs no Azure access or running environment. The UI tests need a reachable environment and a login, so they are run on demand against whichever environment you name. Deployment is run on demand and needs an Azure service connection.

**Pipelines already registered in Azure DevOps** keep the old YAML path after the files moved into `pipelines/`. Edit each one (Pipelines > the pipeline > Edit > ⋮ > Settings, or the pipeline's YAML file path setting) and point it at `/pipelines/pipeline-ci-build-unit-test-dev-ops.yml`, `/pipelines/pipeline-deploy-on-azure-cloud.yml` or `/pipelines/pipeline-ui-tests-external-env.yml`; otherwise the push trigger and manual runs fail with "file not found".

## 1. `pipelines/pipeline-ci-build-unit-test-dev-ops.yml` (CI build and unit tests)

**Trigger:** every commit to `master`. **Agent:** Microsoft-hosted `windows-latest`.

Steps, in order:

1. **Use .NET 10 SDK** (`UseDotNet@2`, `10.0.x`).
2. **Debug: list projects.** Prints the working directory and every `.csproj`, to help diagnose path problems.
3. **Restore** (`dotnet restore`).
4. **Build** (`dotnet build --configuration Release --no-restore`) for the whole solution.
5. **Run unit tests.** Runs only `AuditTrailReview.UnitTest` (MSTest, EF Core InMemory/Sqlite, so no SQL Server is needed) and writes `.trx` files to the agent's temp folder.
6. **Publish test results** (`PublishTestResults@2`, `condition: always()`), so results show in the Tests tab even when a test fails.

**Intentionally not run:** the Selenium UI tests (`AuditTrailReview.UISeleniumTest`). The step is present but commented out. The suite's own fixture starts the WebApp and its default config needs no pipeline variables. The blocker is that CI has no step to provision the LocalDB database, because the schema is raw SQL scripts and there are no EF migrations (see `AuditTrailReview.Database/Database schema/`). The Playwright suite is not run in the build pipeline either; it has its own manual pipeline, see section 2 below.

CI does not produce deployable artifacts, publish images, or touch a database.

## 2. `pipelines/pipeline-ui-tests-external-env.yml` (Playwright UI tests)

**Trigger:** none (`pr: none` too); queue it manually. **Agent:** Microsoft-hosted `windows-latest` by default, or a self-hosted pool you name. Job timeout: 60 minutes.

Runs the Playwright UI tests (`AuditTrailReview.UIPlaywrightTest`) against an already running environment. The tests do not start a WebApp themselves; they drive a browser (headless Edge) against `baseUrl`.

Register it: Pipelines > New pipeline > Azure Repos Git > Existing Azure Pipelines YAML file > `/pipelines/pipeline-ui-tests-external-env.yml`. Then **Run pipeline** and type in:

| Parameter | Meaning |
|---|---|
| `baseUrl` | Full URL of the environment, e.g. `https://atr-test.example.com` |
| `loginEmail` / `loginPassword` | An existing account in that environment (the seeded admin exists only if the user table was empty at first start) |
| `testFilter` | Optional `dotnet test --filter` expression; blank = all tests |
| `agentPool` / `selfHostedPoolName` | Use a self-hosted pool when the environment is not reachable from the internet |

**Permission:** typing free-text parameters requires the **Edit queue build configuration** permission on this pipeline (Pipelines > the pipeline > ⋮ > Manage security). Without it the Run dialog refuses with "To change variables and free-text parameters, you need the 'Edit queue build configuration' permission". The only alternative is a fixed dropdown of environments with the URL/login stored in variable groups.

The parameters become `ATR_PLAYWRIGHT_BASE_URL`, `ATR_PLAYWRIGHT_EMAIL` and `ATR_PLAYWRIGHT_PASSWORD` (see `TestConfig.cs`). The pipeline fails fast on a missing URL/login or an unreachable site, then publishes the TRX results to the run's Tests tab.

Steps, in order: use .NET 10 SDK; validate the parameters (URL must be http(s), login and password required, password registered as a secret so it is masked); check the environment is reachable (up to 12 attempts, 10 s apart; a redirect or 401/403 counts as up, only network errors and 5xx retry); restore and build the UI test project in Release; run the tests with the optional filter; publish the TRX results (`condition: always()`, fails the run on failed tests).

Limits: the tests upload a small CSV and create a scan, so do not target production. Runtime parameters are not secret: the password is masked in logs but visible in the run's Parameters view. The environment must be reachable from the agent; for a private environment use a self-hosted pool.

## 3. `pipelines/pipeline-deploy-on-azure-cloud.yml` (Azure deployment)

**Trigger:** none; queue it manually from Azure DevOps. **Agent:** Microsoft-hosted `ubuntu-latest` by default, or a self-hosted pool you name.

Starting from only a resource group name, it creates the resource group and all Azure resources (Bicep templates in `deploy/infra`), builds and pushes the four container images, applies the SQL schema, and creates or updates the Container Apps. Running it again with the same resource group is an update; a different resource group is a second, independent environment. The full walkthrough is in `deploy/README.md`.

### Parameters

| Parameter | Default | Meaning |
|---|---|---|
| `resourceGroup` | `rg-gxpaudittrail` | Resource group to create or reuse. All resource names derive from its id, so they are stable across runs |
| `location` | `polandcentral` | Region for **all** resources (passed to both Bicep templates; it is not taken from the resource group). Pick a region your subscription may create resources in; see "Things to keep in mind" |
| `azureSubscription` | `atr-azure` | Azure Resource Manager service connection |
| `adminEmail` | (required) | E-mail of the initial WebApp admin; the password is generated into Key Vault |
| `imageTag` | `auto` | Image tag; `auto` (or empty) uses the build id, which is new on every run |
| `agentPool` | `Azure Pipelines` | Keep for Microsoft-hosted `ubuntu-latest`, or type a self-hosted pool name |
| `openSqlFirewall` | `true` | Temporarily open the SQL firewall for the agent IP while the schema is applied |
| `deployMcp` | `true` | Also build and deploy the MCP server |
| `runSmokeTests` | `true` | Run HTTP checks after the rollout |

### Stages

The order matters: each stage depends on the previous one.

1. **Provision** (Azure resources). Creates the resource group if missing, works out who is running (user or service principal, from the access token, so no Microsoft Graph permission is needed), and deploys `deploy/infra/foundation.bicep` (container registry, Key Vault, Azure SQL, managed identity, Container Apps environment). It then assigns four roles **by name** with `az role assignment create` (the CLI resolves the built-in role ids; a hard-coded `AcrPush` id in Bicep was rejected by ARM): `AcrPush` and `Key Vault Secrets Officer` for the pipeline identity, `AcrPull` and `Key Vault Secrets User` for the apps' managed identity. Finally it seeds two secrets into Key Vault once and keeps them on later runs: `atr-encryption-key` (shared by all apps) and `atr-admin-password`. Neither is printed.
2. **Build** (images). A matrix job builds and pushes `atr-webapp`, `atr-api`, `atr-worker` and, if enabled, `atr-mcp` to the registry created in stage 1, each from its own Dockerfile and tagged with the chosen tag.
3. **Database** (SQL schema). Runs `deploy/Deploy-Database.ps1`: applies the snapshot if the database is empty, then the incremental SQL scripts, and creates the database user for the apps' managed identity (`CREATE USER ... WITH SID`, so no Microsoft Graph permission is needed). The snapshot is an SSMS whole-database export, so only the part between its first `USE [GxPAuditTrail]` and its last `USE [master]` is run; its `CREATE DATABASE ... FILENAME = ...` preamble is not valid on Azure SQL. The schema is applied before the new code runs.
4. **Rollout** (Container Apps). Deploys `deploy/infra/apps.bicep` with the new image tag. Apps are ordered Worker, API, WebApp, MCP, so the worker runs the new code before the API accepts new-format jobs; the worker is pinned to one replica. Then, if `runSmokeTests` is on, it checks that the WebApp URL returns 200 and, for MCP, that `/health` returns 200 and `/mcp` without a key returns 401. Each request times out after 30 seconds, and when any check fails the step prints the recent console logs of every Container App in the resource group, so the cause is in the pipeline log.

### Security model

- The apps reach Azure SQL with a managed identity (Entra-only auth), so no SQL password exists.
- Secrets live in Key Vault and are generated by the pipeline.
- Provision, Database and Rollout run as deployment jobs against the Azure DevOps Environment `atr-<resourceGroup>`, so one approval check on that environment gates every stage that changes Azure.

### One-time setup per Azure DevOps project

- An Azure Resource Manager service connection (default name `atr-azure`) whose identity has Owner, or Contributor plus User Access Administrator, on the subscription or on an existing empty resource group. It creates role assignments (registry push, Key Vault, managed identity), which Contributor alone cannot do.
- An Environment named `atr-<resourceGroup>` (Pipelines > Environments, resource type None), created before the first run. It is not reliably auto-created, and the run fails with "Environment ... could not be found" without it. An approval check is optional.

### Things to keep in mind

- The schema is applied before the new apps roll out. For a release that drops columns, the previous revisions can still be running briefly against the new schema.
- A Worker Service must run wherever the API does, otherwise API upload jobs stay `Queued`; this pipeline always deploys the worker.
- No pipeline step runs the tests. Run `pipelines/pipeline-ci-build-unit-test-dev-ops.yml` (or `dotnet test` locally) before deploying.
- **Region restrictions.** A subscription can be barred from creating resources in a region ("The selected region is currently not accepting new customers", `RequestDisallowedByAzure`). `westeurope` was refused for the first test subscription; `polandcentral` worked. If Provision fails like this, run again with another `location`.
- **Reading the initial admin password.** Subscription Owner is not enough: the vault uses Azure RBAC for secrets, so give yourself **Key Vault Secrets User** on the vault (Access control (IAM)), then open the secret `atr-admin-password`. Change the password after the first login.
- **SQL client package.** The WebApp, API and Worker use `Microsoft.Data.SqlClient` 7.x, whose Entra ID (managed identity) login lives in the separate package `Microsoft.Data.SqlClient.Extensions.Azure`. Both are referenced explicitly at the same version in those three projects. Without it the apps crash at startup with "Cannot find an authentication provider for 'ActiveDirectoryManagedIdentity'". Keep the two versions in step when upgrading.
- **Replica limits.** The WebApp is pinned to one replica because ASP.NET Data Protection keys are not shared between instances (several replicas would break login cookies). The Worker is pinned to one replica by design.
- **Forwarded headers.** The WebApp trusts `X-Forwarded-*` only from the private networks set in `ForwardedHeaders__KnownNetworks` (apps.bicep). After the first deployment, check the user login log shows real client IPs.

## How they fit together

```text
push to master ──> pipeline-ci-build-unit-test-dev-ops.yml ──> build + unit tests   (automatic)

manual run     ──> pipeline-deploy-on-azure-cloud.yml
                     Provision ─> Build images ─> Database ─> Rollout + smoke tests

manual run     ──> pipeline-ui-tests-external-env.yml  ──> Playwright tests against the
                     (point it at the environment just deployed, or any other test URL)
```

A typical release: CI is green on `master`, run the deployment pipeline, then run the UI tests against the new environment's WebApp URL (not production).
