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
    IReadOnlyList<ComplianceFinding> Findings,
    string GenerationMode = GenerationModes.Deterministic);

/// <summary>
/// How a draft was produced, surfaced to the Studio so an operator can tell whether the live
/// Foundry model ran or the offline deterministic designer stood in for it.
/// </summary>
public static class GenerationModes
{
    public const string AzureAi = "azure_ai";
    public const string Deterministic = "deterministic";
    public const string OfflineFallback = "offline_fallback";
}
