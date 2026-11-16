// Foundation for one customer environment, deployed at resource-group scope. Everything that
// must exist BEFORE the images are built and the Container Apps are created:
//   ACR, Key Vault, Azure SQL server + database (Entra-only auth), a user-assigned managed
//   identity used by all four apps, Log Analytics and the Container Apps environment.
// Names are derived from the resource group id, so the same group always yields the same names
// and a re-run only converges. Deployed by pipelines/pipeline-deploy-on-azure-cloud.yml (stage Provision); the
// apps themselves are in apps.bicep.

targetScope = 'resourceGroup'

@description('Azure region for all resources. The pipeline passes its location parameter; defaults to the resource group\'s region.')
param location string = resourceGroup().location

@description('Object id of the pipeline identity (the service connection). Becomes the SQL Entra admin. (The pipeline also grants it AcrPush and Key Vault Secrets Officer, outside this template.)')
param deployerObjectId string

@allowed(['Application', 'User'])
param deployerPrincipalType string = 'Application'

@description('Login name shown for the SQL Entra admin (display only).')
param deployerLogin string = 'atr-pipeline'

param tenantId string = tenant().tenantId

@description('Azure SQL database SKU. S1 is enough for a pilot; raise for production.')
param sqlSkuName string = 'S1'
param sqlSkuTier string = 'Standard'

var uniq = uniqueString(resourceGroup().id)
var acrName = 'acratr${uniq}'                 // 5-50 alphanumeric
var kvName = 'kv-atr-${take(uniq, 12)}'       // 3-24
var sqlServerName = 'sql-atr-${uniq}'
var sqlDatabaseName = 'GxPAuditTrail'
var identityName = 'id-atr-${take(uniq, 8)}'

resource appIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: identityName
  location: location
}

resource acr 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: acrName
  location: location
  sku: { name: 'Basic' }
  properties: { adminUserEnabled: false }
}

resource kv 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: kvName
  location: location
  properties: {
    tenantId: tenantId
    sku: { family: 'A', name: 'standard' }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 30
  }
}

resource sql 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: sqlServerName
  location: location
  properties: {
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    administrators: {
      administratorType: 'ActiveDirectory'
      principalType: deployerPrincipalType
      login: deployerLogin
      sid: deployerObjectId
      tenantId: tenantId
      azureADOnlyAuthentication: true
    }
  }
}

resource db 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sql
  name: sqlDatabaseName
  location: location
  sku: { name: sqlSkuName, tier: sqlSkuTier }
  properties: { collation: 'SQL_Latin1_General_CP1_CI_AS' }
}

// "Allow Azure services": lets the Container Apps reach the server (they have no fixed IP).
// Access is still Entra-only, and only the apps' managed identity has a database user.
resource allowAzure 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sql
  name: 'AllowAzureServices'
  properties: { startIpAddress: '0.0.0.0', endIpAddress: '0.0.0.0' }
}

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-atr-${take(uniq, 8)}'
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource env 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: 'cae-atr-${take(uniq, 8)}'
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logs.properties.customerId
        sharedKey: logs.listKeys().primarySharedKey
      }
    }
  }
}

// Role assignments (AcrPull/KV Secrets User for the apps' identity, AcrPush/KV Secrets Officer for
// the pipeline identity) are made by name in the pipeline's Provision step with `az role assignment
// create`, which resolves the built-in role ids itself instead of hard-coding them here.

output location string = location
output acrId string = acr.id
output acrName string = acr.name
output acrLoginServer string = acr.properties.loginServer
output keyVaultId string = kv.id
output keyVaultName string = kv.name
output keyVaultUri string = kv.properties.vaultUri
output sqlServerName string = sql.name
output sqlServerFqdn string = sql.properties.fullyQualifiedDomainName
output sqlDatabaseName string = db.name
output identityName string = appIdentity.name
output identityId string = appIdentity.id
output identityClientId string = appIdentity.properties.clientId
output identityPrincipalId string = appIdentity.properties.principalId
output environmentName string = env.name
