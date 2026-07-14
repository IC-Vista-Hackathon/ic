// Holds the published Payer Experience SPAs (static assets) for every biller. A single router
// workload in AKS serves the correct biller's SPA out of this account based on the request —
// so there's one shared bucket of static content instead of per-biller compute. The router pods
// only serve static files; they do no logic, so they just need read access to blobs here.
param name string
param location string
param workloadIdentityPrincipalId string

// Extra service principals granted blob access on top of the shared `ic-workload` identity —
// e.g. dev/tooling principals that publish or read Payer Experience SPAs directly.
param additionalBlobContributorPrincipalIds array = []
param additionalBlobReaderPrincipalIds array = []

// One container holds all billers' published SPAs, keyed by biller_id/slug prefix (the Deployment
// Service writes each biller's build under its own prefix; the router reads by prefix per request).
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
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: account
  name: 'default'
}

resource container 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: containerName
  properties: {
    publicAccess: 'None'
  }
}

// Storage Blob Data Contributor: the Deployment Service (publish/reconcile) writes each biller's
// SPA here and the router reads it — both run as the shared `ic-workload` identity, so one grant
// covers both.
var blobDataContributorRoleId = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
// Storage Blob Data Reader: read-only blob access for principals that only need to serve/read SPAs.
var blobDataReaderRoleId = '2a2b9908-6ea1-4ae2-8e65-a410df84e7d1'

resource blobAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: account
  name: guid(account.id, workloadIdentityPrincipalId, blobDataContributorRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', blobDataContributorRoleId)
    principalId: workloadIdentityPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource additionalBlobContributorAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for principalId in additionalBlobContributorPrincipalIds: {
  scope: account
  name: guid(account.id, principalId, blobDataContributorRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', blobDataContributorRoleId)
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}]

resource additionalBlobReaderAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for principalId in additionalBlobReaderPrincipalIds: {
  scope: account
  name: guid(account.id, principalId, blobDataReaderRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', blobDataReaderRoleId)
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}]

output id string = account.id
output name string = account.name
output blobEndpoint string = account.properties.primaryEndpoints.blob
output containerName string = container.name
