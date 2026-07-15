param clusterName string
param location string
param dnsPrefix string
param logAnalyticsWorkspaceId string
param acrName string
param nodeCountMin int
param nodeCountMax int
param vmSize string
param uamiName string
param nonprodUamiName string
param workloadNamespace string
param nonprodWorkloadNamespace string
param workloadServiceAccountName string
param publisherUamiName string
param publisherNamespace string
param publisherServiceAccountName string
param deploymentPrincipalIds array
param monitorWorkspaceId string
param monitorWorkspaceLocation string

resource acr 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = {
  name: acrName
}

resource uami 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
  name: uamiName
}

resource nonprodUami 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
  name: nonprodUamiName
}

resource publisherUami 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
  name: publisherUamiName
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
    aadProfile: {
      managed: true
      enableAzureRBAC: true
    }
    addonProfiles: {
      omsagent: {
        enabled: true
        config: { logAnalyticsWorkspaceResourceID: logAnalyticsWorkspaceId }
      }
    }
    // Azure Monitor managed Prometheus — no self-hosted Prometheus/collector in-cluster. The
    // dataCollectionEndpoint/dataCollectionRule/association below are the required plumbing that
    // routes scraped metrics to the Azure Monitor workspace.
    azureMonitorProfile: {
      metrics: {
        enabled: true
        kubeStateMetrics: {}
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

// CI deploy identities authenticate as normal cluster users through Entra + Azure RBAC —
// auditable and revocable, never the exportable system:masters administrator certificate.
// They need RBAC *Cluster Admin* (not Writer): the overlays themselves apply Role/RoleBinding
// objects (e.g. ic-bundle-build-runner) and Gateway API CRs (HTTPRoute), which RBAC Writer's
// data actions do not cover.
resource deploymentClusterUserRoleAssignments 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for principalId in deploymentPrincipalIds: {
  name: guid(aks.id, string(principalId), 'AksClusterUser')
  scope: aks
  properties: {
    principalId: string(principalId)
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4abbcc35-e782-43d8-92c5-2d3f1bd2253f')
  }
}]

resource deploymentRbacClusterAdminRoleAssignments 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for principalId in deploymentPrincipalIds: {
  name: guid(aks.id, string(principalId), 'AksRbacClusterAdmin')
  scope: aks
  properties: {
    principalId: string(principalId)
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'b1ff04bb-8a4e-4dc4-8eb5-8693973ce19b')
  }
}]

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

// Dedicated nonprod identity, federated to the nonprod namespace's service account so per-PR
// (ic-nonprod) pods authenticate to the nonprod Cosmos account only. Because this lives on its
// own identity (not the prod uami), each identity carries a single federated credential and no
// serialization/dependsOn is needed — this sidesteps the
// ConcurrentFederatedIdentityCredentialsWritesForSingleManagedIdentity error entirely.
resource nonprodFederatedCredential 'Microsoft.ManagedIdentity/userAssignedIdentities/federatedIdentityCredentials@2023-01-31' = {
  parent: nonprodUami
  name: 'aks-${nonprodWorkloadNamespace}-${workloadServiceAccountName}'
  properties: {
    issuer: aks.properties.oidcIssuerProfile.issuerURL
    subject: 'system:serviceaccount:${nonprodWorkloadNamespace}:${workloadServiceAccountName}'
    audiences: [ 'api://AzureADTokenExchange' ]
  }
}

resource publisherFederatedCredential 'Microsoft.ManagedIdentity/userAssignedIdentities/federatedIdentityCredentials@2023-01-31' = {
  parent: publisherUami
  name: 'aks-${publisherNamespace}-${publisherServiceAccountName}'
  properties: {
    issuer: aks.properties.oidcIssuerProfile.issuerURL
    subject: 'system:serviceaccount:${publisherNamespace}:${publisherServiceAccountName}'
    audiences: [ 'api://AzureADTokenExchange' ]
  }
}

// Managed Prometheus's data-collection plumbing: a DCE/DCR pair (kind 'Linux', the current
// documented shape for AKS metric collection — see
// https://learn.microsoft.com/azure/azure-monitor/containers/kubernetes-monitoring-enable)
// that forwards the Microsoft-PrometheusMetrics stream to the Azure Monitor workspace, plus the
// association that attaches the DCR to this cluster.
resource prometheusDce 'Microsoft.Insights/dataCollectionEndpoints@2022-06-01' = {
  name: 'dce-${clusterName}-prom'
  location: monitorWorkspaceLocation
  kind: 'Linux'
  properties: {}
}

resource prometheusDcr 'Microsoft.Insights/dataCollectionRules@2022-06-01' = {
  name: 'dcr-${clusterName}-prom'
  location: monitorWorkspaceLocation
  kind: 'Linux'
  properties: {
    dataCollectionEndpointId: prometheusDce.id
    dataSources: {
      prometheusForwarder: [
        {
          name: 'PrometheusDataSource'
          streams: [ 'Microsoft-PrometheusMetrics' ]
        }
      ]
    }
    dataFlows: [
      {
        destinations: [ 'MonitoringAccount1' ]
        streams: [ 'Microsoft-PrometheusMetrics' ]
      }
    ]
    destinations: {
      monitoringAccounts: [
        {
          accountResourceId: monitorWorkspaceId
          name: 'MonitoringAccount1'
        }
      ]
    }
  }
}

resource prometheusDcra 'Microsoft.Insights/dataCollectionRuleAssociations@2022-06-01' = {
  name: 'dcra-${clusterName}-prom'
  scope: aks
  properties: {
    dataCollectionRuleId: prometheusDcr.id
    description: 'Association of the managed Prometheus data collection rule to this AKS cluster.'
  }
}

output name string = aks.name
output id string = aks.id
output oidcIssuerUrl string = aks.properties.oidcIssuerProfile.issuerURL
