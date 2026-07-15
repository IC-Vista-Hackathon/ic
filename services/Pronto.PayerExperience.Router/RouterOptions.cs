namespace Pronto.PayerExperience.Router;

public sealed class RouterOptions
{
    public const string SectionName = "Router";

    // Blob endpoint of the payer-experiences storage account; read with workload identity.
    public string StorageEndpoint { get; init; } = string.Empty;
    public string ContainerName { get; init; } = "payer-experiences";

    // How long a resolved active.json pointer is cached before re-reading, so a freshly
    // published revision goes live within this window without a router restart.
    public int ActivePointerCacheSeconds { get; init; } = 15;
}
