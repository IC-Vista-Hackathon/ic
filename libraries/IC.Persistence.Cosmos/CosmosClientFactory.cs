using Azure.Identity;
using Microsoft.Azure.Cosmos;

namespace IC.Persistence.Cosmos;

/// <summary>
/// Builds a <see cref="CosmosClient"/> authenticated with workload identity
/// (<see cref="DefaultAzureCredential"/>) — no connection strings or keys, matching
/// IC.BillerExperience.Api. In-cluster the AKS workload-identity webhook supplies the
/// federated token; local dev falls back to <c>az login</c>.
/// </summary>
public static class CosmosClientFactory
{
    public static CosmosClient Create(CosmosPersistenceOptions options, string applicationName)
    {
        if (string.IsNullOrWhiteSpace(options.CosmosEndpoint))
        {
            throw new InvalidOperationException(
                "Persistence:CosmosEndpoint is required for the Cosmos provider.");
        }

        return new CosmosClient(
            options.CosmosEndpoint,
            new DefaultAzureCredential(),
            new CosmosClientOptions
            {
                ApplicationName = applicationName,
                Serializer = new CosmosSystemTextJsonSerializer(CosmosJson.CreateOptions()),
            });
    }
}
