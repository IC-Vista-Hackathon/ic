namespace Pronto.BillerExperience.Contracts.V1.Research;

public sealed record BillerResearchRequest(
    Uri? Website,
    string Purpose,
    int MaxPages = 5,
    string? BillerName = null,
    string? BillType = null,
    string? PostalCode = null);

public sealed record BillerResearchResponse(
    ResearchOutcome Outcome,
    IReadOnlyList<ResearchFact> Facts,
    IReadOnlyList<ResearchSource> Sources,
    IReadOnlyList<string> Warnings,
    string? ErrorCode = null,
    bool Retryable = false);

public sealed record ResearchFact(
    string Name,
    string Value,
    Uri SourceUrl,
    double Confidence);

public sealed record ResearchSource(
    Uri Url,
    string? Title,
    DateTimeOffset RetrievedAt);

public enum ResearchOutcome
{
    Completed,
    Degraded,
    Failed,
    Skipped
}
