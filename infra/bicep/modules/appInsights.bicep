// Workspace-based Application Insights (not classic) — traces/logs land in the same
// log-ic-hack Log Analytics workspace that already backs Container Insights. Services use the
// Azure Monitor OpenTelemetry Distro and just need the connection string output below; no
// in-cluster OTEL collector required.
param name string
param location string
param logAnalyticsWorkspaceId string

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: name
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalyticsWorkspaceId
    IngestionMode: 'LogAnalytics'
  }
}

output id string = appInsights.id
output name string = appInsights.name
output connectionString string = appInsights.properties.ConnectionString
