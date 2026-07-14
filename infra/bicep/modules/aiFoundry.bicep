// Unified AI Foundry resource (account + project), not the older separate Hub/Workspace model —
// no extra storage/App Insights/Key Vault needed just to stand up agents.
param name string
param location string
param projectName string = 'ic'
param workloadIdentityPrincipalId string

resource account 'Microsoft.CognitiveServices/accounts@2025-04-01-preview' = {
  name: name
  location: location
  kind: 'AIServices'
  sku: { name: 'S0' }
  identity: { type: 'SystemAssigned' }
  properties: {
    customSubDomainName: name
    publicNetworkAccess: 'Enabled'
    allowProjectManagement: true
  }
}

resource project 'Microsoft.CognitiveServices/accounts/projects@2025-04-01-preview' = {
  parent: account
  name: projectName
  location: location
  identity: { type: 'SystemAssigned' }
  properties: {}
}

// Two tiers: gpt-5.4 for agents that plan/decide/touch money (Onboarding, Financial Planning,
// Policy, Execution); gpt-5.4-mini for narrower single-purpose reads (Research, Aesthetics,
// Compliance, Bill Intelligence). Both current GA, tool-calling capable. Skipped the brand-new
// gpt-5.6 line (GA 5 days ago) and the deprecating gpt-4o/gpt-5.1 — too fresh or already sunsetting.
resource gpt54 'Microsoft.CognitiveServices/accounts/deployments@2025-04-01-preview' = {
  parent: account
  name: 'gpt-5.4'
  sku: { name: 'GlobalStandard', capacity: 10 }
  properties: {
    model: { format: 'OpenAI', name: 'gpt-5.4', version: '2026-03-05' }
  }
}

resource gpt54Mini 'Microsoft.CognitiveServices/accounts/deployments@2025-04-01-preview' = {
  parent: account
  name: 'gpt-5.4-mini'
  sku: { name: 'GlobalStandard', capacity: 10 }
  properties: {
    model: { format: 'OpenAI', name: 'gpt-5.4-mini', version: '2026-03-17' }
  }
  dependsOn: [ gpt54 ]
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
output primaryModelDeployment string = gpt54.name
output miniModelDeployment string = gpt54Mini.name
