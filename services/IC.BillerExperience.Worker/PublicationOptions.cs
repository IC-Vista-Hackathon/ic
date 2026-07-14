namespace IC.BillerExperience.Worker;

public sealed class PublicationOptions
{
    public const string SectionName = "Publication";
    public string CosmosEndpoint { get; init; } = string.Empty;
    public string DatabaseName { get; init; } = "ic";
    public string StorageEndpoint { get; init; } = string.Empty;
    public string ContainerName { get; init; } = "payer-experiences";
    public string PublicBaseUrl { get; init; } = "http://localhost:8080";
    public int PollIntervalSeconds { get; init; } = 5;
}
