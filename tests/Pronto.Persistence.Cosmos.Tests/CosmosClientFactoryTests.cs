using Xunit;

namespace Pronto.Persistence.Cosmos.Tests;

public sealed class CosmosClientFactoryTests
{
    [Fact]
    public void MissingEndpointThrows()
    {
        var options = new CosmosPersistenceOptions { Provider = "Cosmos", CosmosEndpoint = "" };

        var exception = Assert.Throws<InvalidOperationException>(
            () => CosmosClientFactory.Create(options, "test-app"));

        Assert.Contains("CosmosEndpoint", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateConfiguresApplicationNameAndSerializer()
    {
        // CosmosClient construction is lazy — no network happens here.
        var options = new CosmosPersistenceOptions
        {
            Provider = "Cosmos",
            CosmosEndpoint = "https://example.documents.azure.com:443/",
        };

        using var client = CosmosClientFactory.Create(options, "test-app");

        Assert.Equal("test-app", client.ClientOptions.ApplicationName);
        Assert.IsType<CosmosSystemTextJsonSerializer>(client.ClientOptions.Serializer);
    }

    [Fact]
    public void UseCosmosTogglesOnProviderCaseInsensitively()
    {
        Assert.True(new CosmosPersistenceOptions { Provider = "cosmos" }.UseCosmos);
        Assert.True(new CosmosPersistenceOptions { Provider = "Cosmos" }.UseCosmos);
        Assert.False(new CosmosPersistenceOptions { Provider = "InMemory" }.UseCosmos);
        Assert.False(new CosmosPersistenceOptions().UseCosmos);
    }
}
