using Pronto.BillerExperience.Api.Domain;
using Pronto.BillerExperience.Contracts.V1.Experiences;
using Pronto.BillerExperience.Contracts.V1.Onboarding;
using Pronto.BillerExperience.Contracts.V1.Research;

namespace Pronto.BillerExperience.Api.Infrastructure.AI;

public interface IExperienceDraftGenerator
{
    string Provider { get; }

    ValueTask<DraftGenerationResult> GenerateAsync(
        BillerRecord biller,
        ExperienceRecord current,
        IReadOnlyList<OnboardingChatMessage> messages,
        BillerResearchResponse research,
        CancellationToken cancellationToken);
}

public sealed record DraftGenerationResult(
    string Reply,
    BillerExperienceDefinition Definition,
    IReadOnlyList<string> MissingFields,
    IReadOnlyList<ComplianceFinding> Findings);
