// Hackathon sandbox infra: Cosmos DB, AI Foundry, and an AKS cluster to run the services/agents.
// Deploy: az deployment sub create --location $LOCATION --template-file main.bicep
targetScope = 'subscription'

param prefix string = 'ic-hack'
param location string = 'eastus2'
param workloadNamespace string = 'ic'
param workloadServiceAccountName string = 'ic-workload'
param publisherServiceAccountName string = 'biller-publisher'
param aksNodeCountMin int = 2
param aksNodeCountMax int = 4
param aksVmSize string = 'Standard_D2s_v3'

var suffix = uniqueString(subscription().subscriptionId, prefix)

resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: 'rg-${prefix}'
  location: location
}

module logAnalytics 'modules/logAnalytics.bicep' = {
  name: 'logAnalytics'
  scope: rg
  params: {
    name: 'log-${prefix}'
    location: location
  }
}

module acr 'modules/acr.bicep' = {
  name: 'acr'
  scope: rg
  params: {
    // ACR names must be globally unique alphanumeric, no hyphens.
    name: 'acr${replace(prefix, '-', '')}${suffix}'
    location: location
  }
}

module workloadIdentity 'modules/workloadIdentity.bicep' = {
  name: 'workloadIdentity'
  scope: rg
  params: {
    name: 'uami-${prefix}-workload'
    location: location
  }
}

module publisherIdentity 'modules/workloadIdentity.bicep' = {
  name: 'publisherIdentity'
  scope: rg
  params: {
    name: 'uami-${prefix}-publisher'
    location: location
  }
}

module storage 'modules/storage.bicep' = {
  name: 'storage'
  scope: rg
  params: {
    // Storage account names must be globally unique, 3-24 chars, lowercase alphanumeric, no hyphens.
    name: 'st${replace(prefix, '-', '')}${suffix}'
    location: location
    writerPrincipalId: publisherIdentity.outputs.principalId
    readerPrincipalId: workloadIdentity.outputs.principalId
  }
}

module cosmos 'modules/cosmos.bicep' = {
  name: 'cosmos'
  scope: rg
  params: {
    name: 'cosmos-${prefix}-${suffix}'
    location: location
    dataContributorPrincipalIds: [
      workloadIdentity.outputs.principalId
      publisherIdentity.outputs.principalId
    ]
  }
}

module aiFoundry 'modules/aiFoundry.bicep' = {
  name: 'aiFoundry'
  scope: rg
  params: {
    name: 'aif-${prefix}-${suffix}'
    location: location
    workloadIdentityPrincipalId: workloadIdentity.outputs.principalId
    appInsightsId: appInsights.outputs.id
    appInsightsConnectionString: appInsights.outputs.connectionString
  }
}

module appInsights 'modules/appInsights.bicep' = {
  name: 'appInsights'
  scope: rg
  params: {
    name: 'appi-${prefix}'
    location: location
    logAnalyticsWorkspaceId: logAnalytics.outputs.id
  }
}

module monitorWorkspace 'modules/monitorWorkspace.bicep' = {
  name: 'monitorWorkspace'
  scope: rg
  params: {
    name: 'amw-${prefix}'
    location: location
  }
}

module aks 'modules/aks.bicep' = {
  name: 'aks'
  scope: rg
  params: {
    clusterName: 'aks-${prefix}'
    location: location
    dnsPrefix: '${prefix}-${suffix}'
    logAnalyticsWorkspaceId: logAnalytics.outputs.id
    acrName: acr.outputs.name
    nodeCountMin: aksNodeCountMin
    nodeCountMax: aksNodeCountMax
    vmSize: aksVmSize
    uamiName: workloadIdentity.outputs.name
    workloadNamespace: workloadNamespace
    workloadServiceAccountName: workloadServiceAccountName
    publisherUamiName: publisherIdentity.outputs.name
    publisherNamespace: workloadNamespace
    publisherServiceAccountName: publisherServiceAccountName
    monitorWorkspaceId: monitorWorkspace.outputs.id
    monitorWorkspaceLocation: location
  }
}

module grafana 'modules/grafana.bicep' = {
  name: 'grafana'
  scope: rg
  params: {
    name: 'graf-${prefix}'
    location: location
    monitorWorkspaceId: monitorWorkspace.outputs.id
  }
}

output resourceGroup string = rg.name
output cosmosEndpoint string = cosmos.outputs.endpoint
output aiFoundryEndpoint string = aiFoundry.outputs.endpoint
output acrLoginServer string = acr.outputs.loginServer
output payerExperienceBlobEndpoint string = storage.outputs.blobEndpoint
output payerExperienceContainer string = storage.outputs.containerName
output aksClusterName string = aks.outputs.name
output workloadIdentityClientId string = workloadIdentity.outputs.clientId
output publisherIdentityClientId string = publisherIdentity.outputs.clientId
output appInsightsConnectionString string = appInsights.outputs.connectionString
output monitorWorkspaceId string = monitorWorkspace.outputs.id
output grafanaEndpoint string = grafana.outputs.endpoint
