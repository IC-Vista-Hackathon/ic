namespace IC.BillerExperience.Contracts.V1.AgentContext;

public sealed record AgentContextSnapshot(
    string BillerId,
    string RunId,
    long Version,
    string Goal,
    IReadOnlyList<AgentContextEntry> Entries,
    DateTimeOffset UpdatedAt);

public sealed record AgentContextEntry(
    string EntryId,
    AgentContextEntryKind Kind,
    string AgentId,
    string Scope,
    string Content,
    IReadOnlyList<Uri> Sources,
    bool External,
    DateTimeOffset CreatedAt);

public sealed record AppendAgentContextRequest(
    long ExpectedVersion,
    AgentContextEntryKind Kind,
    string AgentId,
    string Scope,
    string Content,
    IReadOnlyList<Uri> Sources,
    bool External = false);

public enum AgentContextEntryKind
{
    Observation,
    CandidateArtifact,
    AcceptedArtifact,
    Correction,
    UnresolvedQuestion
}
