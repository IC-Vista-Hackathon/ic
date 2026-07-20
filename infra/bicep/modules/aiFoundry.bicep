// Unified AI Foundry resource (account + project), not the older separate Hub/Workspace model —
// no extra storage/App Insights/Key Vault needed just to stand up agents.
param name string
param location string
param projectName string = 'ic'
param workloadIdentityPrincipalId string
param foundryOwnerPrincipalIds array = []
param appInsightsId string
@secure()
param appInsightsConnectionString string
@allowed([
  true
  false
])
param mcpConnectionEnabled bool = false
param mcpServerUrl string = ''
@secure()
param mcpApiKey string = ''

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
  sku: { name: 'GlobalStandard', capacity: 500 }
  properties: {
    model: { format: 'OpenAI', name: 'gpt-5.4', version: '2026-03-05' }
  }
}

resource gpt54Mini 'Microsoft.CognitiveServices/accounts/deployments@2025-04-01-preview' = {
  parent: account
  name: 'gpt-5.4-mini'
  sku: { name: 'GlobalStandard', capacity: 500 }
  properties: {
    model: { format: 'OpenAI', name: 'gpt-5.4-mini', version: '2026-03-17' }
  }
  dependsOn: [ gpt54 ]
}

// Wires the project to App Insights so Foundry Observability (evaluation/monitoring/tracing)
// actually has somewhere to publish — without this connection, App Insights just sits in the
// resource group unconnected, and the Foundry portal's Observability dashboard stays empty.
resource appInsightsConnection 'Microsoft.CognitiveServices/accounts/projects/connections@2025-04-01-preview' = {
  parent: project
  name: 'appinsights'
  properties: {
    category: 'AppInsights'
    target: appInsightsId
    authType: 'ApiKey'
    isSharedToAll: false
    credentials: {
      key: appInsightsConnectionString
    }
    metadata: {
      ApiType: 'Azure'
      ResourceId: appInsightsId
    }
  }
}

// Shared biller/run context exposed to prompt agents as a remote MCP tool. The project
// connection stores only the server-level demo credential. Orchestration separately issues a
// short-lived capability token bound to the biller, run, agent, and write permission.
resource sharedContextMcpConnection 'Microsoft.CognitiveServices/accounts/projects/connections@2026-03-01' = if (mcpConnectionEnabled) {
  parent: project
  name: 'ic-shared-context-mcp'
  properties: {
    category: 'RemoteTool'
    target: mcpServerUrl
    authType: 'CustomKeys'
    isSharedToAll: false
    credentials: {
      keys: {
        'X-IC-MCP-Key': mcpApiKey
      }
    }
    metadata: {
      Kind: 'RemoteMCP'
      ServerLabel: 'ic_shared_context'
      // Connection-level superset of the MCP tools any agent may be granted. The per-agent
      // allowlist is derived from each agent's tools.json at provisioning time
      // (FoundryAgentReconciler), so an individual agent only ever receives the subset it declares.
      AllowedTools: 'get_goal_context,append_context,get_biller_configuration,list_invoices,get_invoice,get_payment_quote,verify_payer_account,get_payer_profile,get_payment_history,update_payer_preferences,bind_execution_capability,create_payment_intent,submit_payment,seed_invoices,register_payer'
      ResponsibleAiPolicy: 'agents/RESPONSIBLE_AI.md'
    }
  }
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

resource foundryAgentConsumerRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(account.id, workloadIdentityPrincipalId, 'FoundryAgentConsumer')
  scope: account
  properties: {
    principalId: workloadIdentityPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'eed3b665-ab3a-47b6-8f48-c9382fb1dad6')
  }
}

resource foundryOwnerRoleAssignments 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for principalId in foundryOwnerPrincipalIds: {
  name: guid(account.id, principalId, 'FoundryOwner')
  scope: account
  properties: {
    principalId: principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'c883944f-8b7b-4483-af10-35834be79c4a')
  }
}]

output id string = account.id
output endpoint string = account.properties.endpoint
output projectName string = project.name
output primaryModelDeployment string = gpt54.name
output miniModelDeployment string = gpt54Mini.name
output sharedContextMcpConnectionName string = mcpConnectionEnabled ? sharedContextMcpConnection.name : ''
// Agent versions are data-plane resources. Their provisioning code consumes this exact tool
// definition so the allowlist and approval behavior remain infrastructure-reviewed.
output sharedContextMcpTool object = mcpConnectionEnabled ? {
  type: 'mcp'
  server_label: 'ic_shared_context'
  server_url: mcpServerUrl
  allowed_tools: [
    'get_goal_context'
    'append_context'
  ]
  // Demo mode: writes remain constrained by scoped capabilities and server-side validation.
  require_approval: 'never'
  project_connection_id: sharedContextMcpConnection.name
} : {}
