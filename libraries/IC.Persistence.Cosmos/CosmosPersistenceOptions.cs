namespace IC.Persistence.Cosmos;

/// <summary>
/// Persistence configuration for an IC service host. Bound from the <c>Persistence</c>
/// config section (env <c>Persistence__Provider</c>, <c>Persistence__CosmosEndpoint</c>,
/// <c>Persistence__DatabaseName</c>). Mirrors IC.BillerExperience.Api's provider toggle.
/// </summary>
public sealed class CosmosPersistenceOptions
{
    public const string SectionName = "Persistence";

    /// <summary><c>InMemory</c> (default) or <c>Cosmos</c>.</summary>
    public string Provider { get; set; } = "InMemory";

    /// <summary>Cosmos account endpoint; required when <see cref="UseCosmos"/> is true.</summary>
    public string CosmosEndpoint { get; set; } = string.Empty;

    public string DatabaseName { get; set; } = "ic";

    public bool UseCosmos => string.Equals(Provider, "Cosmos", StringComparison.OrdinalIgnoreCase);
}
