namespace IC.Agentic.Orchestration.Abstractions;

public sealed record OrchestrationContext(
    string RunId,
    string CorrelationId,
    string? BillerId = null,
    string? SessionId = null)
{
    public static OrchestrationContext Create(
        string? billerId = null,
        string? sessionId = null,
        string? correlationId = null) =>
        new(
            Guid.NewGuid().ToString("N"),
            correlationId ?? Guid.NewGuid().ToString("N"),
            billerId,
            sessionId);
}
