// Holds immutable Payer Experience definitions and manifests for every biller. The shared PWA
// renderer reads public-safe artifacts through the Biller Experience API, avoiding per-biller
// compute and keeping the container private.
param name string
param location string
param writerPrincipalId string
param readerPrincipalId string
@description('Deploy Blob data-plane role assignments. Disable only when the deployer lacks roleAssignments/write; apply them later with a privileged identity.')
param deployRoleAssignments bool = true

// One container holds all billers' artifacts, keyed by slug and immutable revision prefix. The
// publisher replaces active.json only after its revision files have been written and verified.
param containerName string = 'payer-experiences'

resource account 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: name
  location: location
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  properties: {
    accessTier: 'Hot'
    // Access is via workload identity (Entra) + RBAC, never keys or anonymous blobs — matches the
    // no-secrets-in-pods convention used for Cosmos/AI Foundry.
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
    defaultToOAuthAuthentication: true
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: account
  name: 'default'
  properties: {
    deleteRetentionPolicy: { enabled: true, days: 7 }
    containerDeleteRetentionPolicy: { enabled: true, days: 7 }
  }
}

resource container 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: containerName
  properties: {
    publicAccess: 'None'
  }
}

// The publisher can write immutable revisions and the active pointer. The API's workload identity
// receives read-only access and proxies public-safe artifacts to the shared renderer.
var blobDataContributorRoleId = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
var blobDataReaderRoleId = '2a2b9908-6ea1-4ae2-8e65-a410df84e7d1'

resource blobWriteAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (deployRoleAssignments) {
  scope: account
  name: guid(account.id, writerPrincipalId, blobDataContributorRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', blobDataContributorRoleId)
    principalId: writerPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource blobReadAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (deployRoleAssignments) {
  scope: account
  name: guid(account.id, readerPrincipalId, blobDataReaderRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', blobDataReaderRoleId)
    principalId: readerPrincipalId
    principalType: 'ServicePrincipal'
  }
}

output id string = account.id
output name string = account.name
output blobEndpoint string = account.properties.primaryEndpoints.blob
output containerName string = container.name
