namespace Pronto.BillerExperience.Api.Configuration;

public sealed class BillerExperienceOptions
{
    public const string SectionName = "BillerExperience";
    public PersistenceOptions Persistence { get; set; } = new();
    public ModelOptions Model { get; set; } = new();
    public PublishedExperienceOptions PublishedExperience { get; set; } = new();
    public SupportingServicesOptions SupportingServices { get; set; } = new();
    public ResearchOptions Research { get; set; } = new();
    public ComplianceOptions Compliance { get; set; } = new();
    public McpOptions Mcp { get; set; } = new();
    public AgentProvisioningOptions AgentProvisioning { get; set; } = new();
}

public sealed class AgentProvisioningOptions
{
    public bool Enabled { get; set; }
    public string DefinitionsPath { get; set; } = "agents";
    public string PrimaryModel { get; set; } = "gpt-5.4";
    public string MiniModel { get; set; } = "gpt-5.4-mini";
    public string McpConnectionId { get; set; } = "ic-shared-context-mcp";
}

public sealed class ComplianceOptions
{
    public string FoundryAgentId { get; set; } = string.Empty;
    public bool RequireFoundryEvidence { get; set; }
    public string PolicyVersion { get; set; } = "2026-07-15";
}

public sealed class McpOptions
{
    public bool Enabled { get; set; }
    public string PublicEndpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string CapabilitySigningKey { get; set; } = string.Empty;
    public int CapabilityLifetimeMinutes { get; set; } = 30;

    /// <summary>
    /// The only agent id permitted to submit a payment. Payment submission is money movement:
    /// design/services.md restricts it to the Execution Agent path, enforced server-side here.
    /// </summary>
    public string ExecutionAgentId { get; set; } = "execution";
}

public sealed class ResearchOptions
{
    public string FoundryProjectEndpoint { get; set; } = string.Empty;
    public string CoordinatorAgentId { get; set; } = string.Empty;
    public int FoundryPollIntervalMilliseconds { get; set; } = 500;
    public int MaxPages { get; set; } = 5;
    public int MaxLinksPerPage { get; set; } = 20;
    public int MaxResponseBytes { get; set; } = 512_000;
    public int RequestTimeoutSeconds { get; set; } = 10;
    public int MaxFactLength { get; set; } = 500;
    public int MaxAgentCount { get; set; } = 4;
    public int MaxParallelAgents { get; set; } = 2;
    public int AgentTimeoutSeconds { get; set; } = 30;
    public string[] AllowedAgentIds { get; set; } = [];
    public string RequiredCapability { get; set; } = "biller_research";
}

public sealed class SupportingServicesOptions
{
    public string InvoiceBaseUrl { get; set; } = string.Empty;
    public string PaymentBaseUrl { get; set; } = string.Empty;
    public string PayerAccountBaseUrl { get; set; } = string.Empty;
}

public sealed class PublishedExperienceOptions
{
    public string StorageEndpoint { get; set; } = string.Empty;
    public string ContainerName { get; set; } = "payer-experiences";
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
