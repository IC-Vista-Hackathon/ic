// Hackathon sandbox infra: Cosmos DB, AI Foundry, and an AKS cluster to run the services/agents.
// Deploy: az deployment sub create --location $LOCATION --template-file main.bicep
targetScope = 'subscription'

param prefix string = 'ic-hack'
param location string = 'eastus2'
param workloadNamespace string = 'ic'
param workloadServiceAccountName string = 'ic-workload'
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

module cosmos 'modules/cosmos.bicep' = {
  name: 'cosmos'
  scope: rg
  params: {
    name: 'cosmos-${prefix}-${suffix}'
    location: location
    workloadIdentityPrincipalId: workloadIdentity.outputs.principalId
  }
}

module aiFoundry 'modules/aiFoundry.bicep' = {
  name: 'aiFoundry'
  scope: rg
  params: {
    name: 'aif-${prefix}-${suffix}'
    location: location
    workloadIdentityPrincipalId: workloadIdentity.outputs.principalId
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
  }
}

output resourceGroup string = rg.name
output cosmosEndpoint string = cosmos.outputs.endpoint
output aiFoundryEndpoint string = aiFoundry.outputs.endpoint
output acrLoginServer string = acr.outputs.loginServer
output aksClusterName string = aks.outputs.name
output workloadIdentityClientId string = workloadIdentity.outputs.clientId
