using Pronto.BillerExperience.Api.Domain;
using Pronto.BillerExperience.Contracts.V1.Onboarding;
using Pronto.BillerExperience.Contracts.V1.Research;
using Pronto.BillerExperience.Contracts.V1.Billing;

namespace Pronto.BillerExperience.Api.Infrastructure.AI;

public sealed partial class FallbackExperienceDraftGenerator(
    AzureExperienceDraftGenerator primary,
    DeterministicExperienceDraftGenerator fallback,
    ILogger<FallbackExperienceDraftGenerator> logger) : IExperienceDraftGenerator
{
    public string Provider => "AzureAI+DeterministicFallback";

    public async ValueTask<DraftGenerationResult> GenerateAsync(
        BillerRecord biller,
        ExperienceRecord current,
        IReadOnlyList<OnboardingChatMessage> messages,
        BillingProfile billingProfile,
        BillerResearchResponse research,
        CancellationToken cancellationToken)
    {
        try
        {
            return await primary.GenerateAsync(biller, current, messages, billingProfile, research, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogFallback(logger, biller.Id, exception);
            var result = await fallback.GenerateAsync(biller, current, messages, billingProfile, research, cancellationToken);
            return result with { GenerationMode = GenerationModes.OfflineFallback };
        }
    }

    [LoggerMessage(2202, LogLevel.Error,
        "Azure AI generation failed for biller {BillerId}; using deterministic fallback")]
    private static partial void LogFallback(ILogger logger, string billerId, Exception exception);
}
