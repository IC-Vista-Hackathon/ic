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
