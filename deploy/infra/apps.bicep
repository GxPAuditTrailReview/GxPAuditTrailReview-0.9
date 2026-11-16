// The four Container Apps of one customer environment. Deployed by pipelines/pipeline-deploy-on-azure-cloud.yml
// (stage Rollout) AFTER foundation.bicep, the image push and the database stage, so every
// revision starts against a ready schema and an image that already exists. Re-running with a new
// imageTag is the rollout; the template is idempotent.
//
// Secrets live in Key Vault and are referenced through the shared managed identity (no secret
// values pass through this template or the pipeline logs). The pipeline seeds them before this
// runs: atr-encryption-key (same value for all four apps -- Security:EncryptionKey) and
// atr-admin-password (initial WebApp admin, SeedAccount:Password).

targetScope = 'resourceGroup'

param location string = resourceGroup().location
param environmentName string
param identityId string
param identityClientId string
param acrLoginServer string
param keyVaultUri string
param sqlServerFqdn string
param sqlDatabaseName string

@description('Image tag built by the pipeline.')
param imageTag string
param deployMcp bool = true

@description('E-mail of the initial admin account (SeedAccount:Email).')
param adminEmail string

@description('Private ranges Container Apps ingress forwards from; trusted for X-Forwarded-* (see ForwardedHeadersSetup).')
param forwardedNetworks array = ['10.0.0.0/8', '100.64.0.0/10', '172.16.0.0/12']

var uniq = uniqueString(resourceGroup().id)
var connectionString = 'Server=tcp:${sqlServerFqdn},1433;Database=${sqlDatabaseName};Authentication=Active Directory Managed Identity;User Id=${identityClientId};Encrypt=True;TrustServerCertificate=False;MultipleActiveResultSets=True'

resource env 'Microsoft.App/managedEnvironments@2024-03-01' existing = {
  name: environmentName
}

var identityBlock = {
  type: 'UserAssigned'
  userAssignedIdentities: { '${identityId}': {} }
}

var registries = [
  { server: acrLoginServer, identity: identityId }
]

var encryptionKeySecret = {
  name: 'encryption-key'
  keyVaultUrl: '${keyVaultUri}secrets/atr-encryption-key'
  identity: identityId
}

var baseEnv = [
  { name: 'ConnectionStrings__DefaultConnection', value: connectionString }
  { name: 'Security__EncryptionKey', secretRef: 'encryption-key' }
]

// Worker: no ingress, exactly one replica (jobs are processed sequentially, ScanUploadJobPoller).
resource worker 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'ca-atr-worker-${take(uniq, 6)}'
  location: location
  identity: identityBlock
  properties: {
    managedEnvironmentId: env.id
    configuration: {
      registries: registries
      secrets: [encryptionKeySecret]
    }
    template: {
      containers: [
        {
          name: 'worker'
          image: '${acrLoginServer}/atr-worker:${imageTag}'
          resources: { cpu: json('1.0'), memory: '2Gi' }
          env: baseEnv
        }
      ]
      scale: { minReplicas: 1, maxReplicas: 1 }
    }
  }
}

// API: external HTTPS ingress (callers use an API key). Updated after the worker.
resource api 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'ca-atr-api-${take(uniq, 6)}'
  location: location
  identity: identityBlock
  dependsOn: [worker]
  properties: {
    managedEnvironmentId: env.id
    configuration: {
      registries: registries
      secrets: [encryptionKeySecret]
      ingress: { external: true, targetPort: 8080, transport: 'auto', allowInsecure: false }
    }
    template: {
      containers: [
        {
          name: 'api'
          image: '${acrLoginServer}/atr-api:${imageTag}'
          resources: { cpu: json('0.5'), memory: '1Gi' }
          env: baseEnv
        }
      ]
      scale: { minReplicas: 1, maxReplicas: 2 }
    }
  }
}

// WebApp: pinned to one replica because ASP.NET Data Protection keys are not shared
// (nothing calls PersistKeysTo*); with 2+ replicas login cookies would randomly fail.
resource web 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'ca-atr-web-${take(uniq, 6)}'
  location: location
  identity: identityBlock
  dependsOn: [api]
  properties: {
    managedEnvironmentId: env.id
    configuration: {
      registries: registries
      secrets: [
        encryptionKeySecret
        {
          name: 'admin-password'
          keyVaultUrl: '${keyVaultUri}secrets/atr-admin-password'
          identity: identityId
        }
      ]
      ingress: { external: true, targetPort: 8080, transport: 'auto', allowInsecure: false }
    }
    template: {
      containers: [
        {
          name: 'webapp'
          image: '${acrLoginServer}/atr-webapp:${imageTag}'
          resources: { cpu: json('1.0'), memory: '2Gi' }
          env: concat(baseEnv, [
            { name: 'SeedAccount__Email', value: adminEmail }
            { name: 'SeedAccount__Password', secretRef: 'admin-password' }
            { name: 'ForwardedHeaders__KnownNetworks__0', value: forwardedNetworks[0] }
            { name: 'ForwardedHeaders__KnownNetworks__1', value: forwardedNetworks[1] }
            { name: 'ForwardedHeaders__KnownNetworks__2', value: forwardedNetworks[2] }
          ])
        }
      ]
      scale: { minReplicas: 1, maxReplicas: 1 }
    }
  }
}

resource mcp 'Microsoft.App/containerApps@2024-03-01' = if (deployMcp) {
  name: 'ca-atr-mcp-${take(uniq, 6)}'
  location: location
  identity: identityBlock
  dependsOn: [web]
  properties: {
    managedEnvironmentId: env.id
    configuration: {
      registries: registries
      secrets: [encryptionKeySecret]
      ingress: { external: true, targetPort: 8080, transport: 'auto', allowInsecure: false }
    }
    template: {
      containers: [
        {
          name: 'mcp'
          image: '${acrLoginServer}/atr-mcp:${imageTag}'
          resources: { cpu: json('0.5'), memory: '1Gi' }
          env: baseEnv
        }
      ]
      scale: { minReplicas: 1, maxReplicas: 2 }
    }
  }
}

output webFqdn string = web.properties.configuration.ingress.fqdn
output apiFqdn string = api.properties.configuration.ingress.fqdn
output mcpFqdn string = mcp.?properties.configuration.ingress.fqdn ?? ''
