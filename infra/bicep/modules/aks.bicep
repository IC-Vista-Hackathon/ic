param clusterName string
param location string
param dnsPrefix string
param logAnalyticsWorkspaceId string
param acrName string
param nodeCountMin int
param nodeCountMax int
param vmSize string
param uamiName string
param workloadNamespace string
param workloadServiceAccountName string

resource acr 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = {
  name: acrName
}

resource uami 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
  name: uamiName
}

resource aks 'Microsoft.ContainerService/managedClusters@2024-02-01' = {
  name: clusterName
  location: location
  identity: { type: 'SystemAssigned' }
  properties: {
    dnsPrefix: dnsPrefix
    // ponytail: kubenet needs no custom VNet/subnet plumbing; move to Azure CNI overlay + a
    // real VNet if this ever becomes more than a hackathon cluster.
    networkProfile: {
      networkPlugin: 'kubenet'
    }
    oidcIssuerProfile: { enabled: true }
    securityProfile: {
      workloadIdentity: { enabled: true }
    }
    addonProfiles: {
      omsagent: {
        enabled: true
        config: { logAnalyticsWorkspaceResourceID: logAnalyticsWorkspaceId }
      }
    }
    agentPoolProfiles: [
      {
        name: 'system'
        mode: 'System'
        osType: 'Linux'
        vmSize: vmSize
        enableAutoScaling: true
        count: nodeCountMin
        minCount: nodeCountMin
        maxCount: nodeCountMax
      }
    ]
  }
}

// Lets AKS pull images from the hackathon's single ACR.
resource acrPullRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(acr.id, aks.id, 'AcrPull')
  scope: acr
  properties: {
    principalId: aks.properties.identityProfile.kubeletidentity.objectId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
  }
}

// Federates the workload identity to a specific namespace/service account — services running
// as this service account can request Azure AD tokens with no secret, via workload identity.
resource federatedCredential 'Microsoft.ManagedIdentity/userAssignedIdentities/federatedIdentityCredentials@2023-01-31' = {
  parent: uami
  name: 'aks-${workloadNamespace}-${workloadServiceAccountName}'
  properties: {
    issuer: aks.properties.oidcIssuerProfile.issuerURL
    subject: 'system:serviceaccount:${workloadNamespace}:${workloadServiceAccountName}'
    audiences: [ 'api://AzureADTokenExchange' ]
  }
}

output name string = aks.name
output id string = aks.id
output oidcIssuerUrl string = aks.properties.oidcIssuerProfile.issuerURL
