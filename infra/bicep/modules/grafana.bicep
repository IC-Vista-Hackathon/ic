// Azure Managed Grafana, wired to read Prometheus metrics from the Azure Monitor workspace.
// System-assigned identity + a Monitoring Reader role assignment (granted in main.bicep, scoped
// to the resource group — Bicep can't scope a role assignment directly to a Monitor workspace
// resource, so resource-group scope is the documented workaround; it's inherited down to the
// workspace) is what lets Grafana query it with no stored credentials.
param name string
param location string
param monitorWorkspaceId string

resource grafana 'Microsoft.Dashboard/grafana@2023-09-01' = {
  name: name
  location: location
  sku: { name: 'Standard' }
  identity: { type: 'SystemAssigned' }
  properties: {
    grafanaIntegrations: {
      azureMonitorWorkspaceIntegrations: [
        { azureMonitorWorkspaceResourceId: monitorWorkspaceId }
      ]
    }
  }
}

// Grafana reads Prometheus data from the Monitor workspace via its managed identity. Bicep can't
// scope a role assignment directly to a Microsoft.Monitor/accounts resource (no REST API spec for
// it), so this is scoped to the resource group (this module's scope) and inherited down — the
// documented workaround; see
// https://learn.microsoft.com/azure/azure-monitor/containers/kubernetes-monitoring-enable#prometheus-metrics-and-container-insights
resource monitoringReaderRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, grafana.id, 'MonitoringReader')
  properties: {
    principalId: grafana.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '43d0d8ad-25c7-4714-9337-8ba259a9fe05')
  }
}

output id string = grafana.id
output name string = grafana.name
output principalId string = grafana.identity.principalId
output endpoint string = grafana.properties.endpoint
