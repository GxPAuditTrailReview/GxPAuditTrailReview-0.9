# Deploying a customer environment to Azure

`pipelines/pipeline-deploy-on-azure-cloud.yml` creates and updates a whole environment from **one resource group name**.

## One-time setup (per Azure DevOps project)

1. Create one **Azure Resource Manager** service connection (workload identity federation recommended).
   Its identity needs **Owner**, or **Contributor + User Access Administrator**, on the subscription
   (or on an existing empty resource group). It creates role assignments, which Contributor cannot.
2. Create an Environment named `atr-<resourceGroup>` (default `atr-rg-gxpaudittrail`, resource type
   None) under Pipelines -> Environments. Required: without it the run fails with "Environment
   ... could not be found". Optionally add an approval check; create one per resource group.
3. Pipelines -> New pipeline -> existing YAML -> `/pipelines/pipeline-deploy-on-azure-cloud.yml`.

## Each deployment

Run the pipeline and fill in `adminEmail`. `azureSubscription` defaults to `atr-azure`, so name
your service connection exactly that (or override the parameter). The
resource group defaults to `rg-gxpaudittrail`; change `resourceGroup` only to deploy a second,
separate environment. `location` (default `polandcentral`) is the region for all resources; if
Azure answers "region not accepting new customers", pick another one. `imageTag` and `deployMcp`
are optional.

| Stage | What it does |
|---|---|
| Provision | Creates the resource group if missing, then `infra/foundation.bicep`: ACR, Key Vault, Azure SQL server + database (Entra-only), managed identity, Log Analytics, Container Apps environment. Seeds `atr-encryption-key` and `atr-admin-password` in Key Vault once. |
| Build | Builds and pushes the four images to the new ACR. |
| Database | `Deploy-Database.ps1`: snapshot if empty, incremental scripts, and the contained DB user (db_owner) for the apps' managed identity. |
| Rollout | `infra/apps.bicep`: Worker, API, WebApp, (MCP) Container Apps on the new tag, then smoke tests. |

Re-running with the same resource group converges; a new `imageTag` (default: build id) is a release.

## After the first run

- WebApp URL is printed by the Rollout stage. Log in as `adminEmail`; the password is the Key Vault
  secret `atr-admin-password` (give yourself the *Key Vault Secrets User* role to read it). Change it on first login.
- Key Vault, SQL and ACR names are derived from the resource group id, so they are stable per group.
- Subscription Owner cannot read Key Vault secrets by itself: grant yourself **Key Vault Secrets User** on the vault first.
- If a Container App crash-loops, the pipeline's smoke-test step prints every app's recent logs. The WebApp, API and Worker need both `Microsoft.Data.SqlClient` and `Microsoft.Data.SqlClient.Extensions.Azure` (same version) for managed-identity SQL login.

Full details, parameters and troubleshooting: `AuditTrailReview.Docs/CICD/Azure Pipelines.md` and `AuditTrailReview.Docs/Tech/Technical Manual.md` section 7.

## Things to know

- **WebApp is pinned to one replica**: Data Protection keys are not persisted/shared, so more replicas would break login cookies.
- **Worker is pinned to one replica** by design (sequential job processing).
- SQL uses a public endpoint with "Allow Azure services" and Entra-only auth. For private networking, add a VNet and private endpoint to `foundation.bicep` and use a self-hosted pool.
- `ForwardedHeaders__KnownNetworks` is set to the private ranges Container Apps ingress comes from. Verify the user login log shows real client IPs after the first deployment.
- The Worker's FTP settings are configured in the app (AdminPanel/Settings), not here. The SMTP password and AI keys are also set in the app.
