namespace IC.BillerExperience.Api.Configuration;

public sealed class BillerExperienceOptions
{
    public const string SectionName = "BillerExperience";
    public PersistenceOptions Persistence { get; set; } = new();
    public ModelOptions Model { get; set; } = new();
}

public sealed class PersistenceOptions
{
    public string Provider { get; set; } = "InMemory";
    public string CosmosEndpoint { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = "ic";
}

public sealed class ModelOptions
{
    public string Provider { get; set; } = "Deterministic";
    public string Endpoint { get; set; } = string.Empty;
    public string Deployment { get; set; } = "gpt-5-mini";
}
