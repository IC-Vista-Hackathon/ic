// Containers match design/entities.md's Cosmos conventions section — one container per entity,
// partitioned on /biller_id except BillerAccount (partitioned on its own /id).
param name string
param location string
param databaseName string = 'ic'
param workloadIdentityPrincipalId string

param containers array = [
  { name: 'billers', partitionKeyPath: '/id' }
  { name: 'configs', partitionKeyPath: '/biller_id' }
  { name: 'deployments', partitionKeyPath: '/biller_id' }
  { name: 'orchestration_runs', partitionKeyPath: '/biller_id' }
  { name: 'payer_accounts', partitionKeyPath: '/biller_id' }
  { name: 'invoices', partitionKeyPath: '/biller_id' }
  { name: 'payments', partitionKeyPath: '/biller_id' }
  { name: 'purchases', partitionKeyPath: '/biller_id' }
  { name: 'notifications', partitionKeyPath: '/biller_id' }
]

resource account 'Microsoft.DocumentDB/databaseAccounts@2024-05-15' = {
  name: name
  location: location
  kind: 'GlobalDocumentDB'
  properties: {
    databaseAccountOfferType: 'Standard'
    // Serverless: no RU planning/scaling for a hackathon, pay only for what you use.
    capabilities: [ { name: 'EnableServerless' } ]
    locations: [
      { locationName: location, failoverPriority: 0, isZoneRedundant: false }
    ]
  }
}

resource database 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2024-05-15' = {
  parent: account
  name: databaseName
  properties: {
    resource: { id: databaseName }
  }
}

resource sqlContainers 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-05-15' = [for c in containers: {
  parent: database
  name: c.name
  properties: {
    resource: {
      id: c.name
      partitionKey: { paths: [ c.partitionKeyPath ], kind: 'Hash' }
    }
  }
}]

// Cosmos data-plane access isn't a normal Azure RBAC role — it's this SQL-role-assignment
// resource, scoped to the account, granting the built-in "Data Contributor" role.
resource dataAccess 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-05-15' = {
  parent: account
  name: guid(account.id, workloadIdentityPrincipalId, 'DataContributor')
  properties: {
    roleDefinitionId: '${account.id}/sqlRoleDefinitions/00000000-0000-0000-0000-000000000002'
    principalId: workloadIdentityPrincipalId
    scope: account.id
  }
}

output id string = account.id
output name string = account.name
output endpoint string = account.properties.documentEndpoint
