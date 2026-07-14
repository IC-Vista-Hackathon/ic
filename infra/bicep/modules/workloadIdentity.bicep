// User-assigned identity federated to an AKS service account (via aks.bicep's federated
// credential) so pods call Cosmos/AI Foundry with no secrets/connection strings in config.
param name string
param location string

resource uami 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: name
  location: location
}

output id string = uami.id
output name string = uami.name
output principalId string = uami.properties.principalId
output clientId string = uami.properties.clientId
