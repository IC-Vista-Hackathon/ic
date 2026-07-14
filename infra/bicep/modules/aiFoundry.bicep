// Unified AI Foundry resource (account + project), not the older separate Hub/Workspace model —
// no extra storage/App Insights/Key Vault needed just to stand up agents.
param name string
param location string
param projectName string = 'ic'
param workloadIdentityPrincipalId string

resource account 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: name
  location: location
  kind: 'AIServices'
  sku: { name: 'S0' }
  identity: { type: 'SystemAssigned' }
  properties: {
    customSubDomainName: name
    publicNetworkAccess: 'Enabled'
  }
}

resource project 'Microsoft.CognitiveServices/accounts/projects@2025-04-01-preview' = {
  parent: account
  name: projectName
  location: location
  identity: { type: 'SystemAssigned' }
  properties: {}
}

// Lets services/agents call the AI Foundry project's inference endpoints with no API key.
resource cognitiveServicesUserRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(account.id, workloadIdentityPrincipalId, 'CognitiveServicesUser')
  scope: account
  properties: {
    principalId: workloadIdentityPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'a97b65f3-24c7-4388-baec-2e87135dc908')
  }
}

output id string = account.id
output endpoint string = account.properties.endpoint
output projectName string = project.name
