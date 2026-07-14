using IC.BillerExperience.Api.Domain;
using IC.BillerExperience.Contracts.V1.Experiences;
using IC.BillerExperience.Contracts.V1.Onboarding;

namespace IC.BillerExperience.Api.Infrastructure.AI;

public interface IExperienceDraftGenerator
{
    string Provider { get; }

    ValueTask<DraftGenerationResult> GenerateAsync(
        BillerRecord biller,
        ExperienceRecord current,
        IReadOnlyList<OnboardingChatMessage> messages,
        CancellationToken cancellationToken);
}

public sealed record DraftGenerationResult(
    string Reply,
    BillerExperienceDefinition Definition,
    IReadOnlyList<string> MissingFields,
    IReadOnlyList<ComplianceFinding> Findings);
