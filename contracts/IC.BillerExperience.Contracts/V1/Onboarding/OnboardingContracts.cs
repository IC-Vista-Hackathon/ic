namespace IC.BillerExperience.Contracts.V1.Onboarding;

public sealed record StartOnboardingRequest(string? BillerId = null);

public sealed record SendOnboardingMessageRequest(string Message);

public sealed record OnboardingChatMessage(
    string Role,
    string Content,
    DateTimeOffset CreatedAt);

public sealed record OnboardingChatResponse(
    string Reply,
    OnboardingSessionResponse Session,
    Experiences.ExperienceRevisionResponse? Draft);

public sealed record AgentActivityEvent(
    string EventId,
    long Sequence,
    string RunId,
    string AgentId,
    string DisplayName,
    AgentActivityStatus Status,
    string Summary,
    DateTimeOffset OccurredAt,
    string? TraceId = null,
    string? ErrorCode = null,
    bool Retryable = false,
    int Attempt = 1,
    double? DurationMs = null);

public enum AgentActivityStatus
{
    Discovered,
    Queued,
    Running,
    Completed,
    NeedsInput,
    Failed,
    Retrying,
    Degraded
}

public sealed record OnboardingSessionResponse(
    string SessionId,
    string BillerId,
    OnboardingSessionState State,
    IReadOnlyList<string> MissingFields,
    DateTimeOffset UpdatedAt);

public enum OnboardingSessionState
{
    CollectingInformation,
    DraftReady,
    AwaitingApproval,
    Approved,
    Publishing,
    Published,
    Failed
}
