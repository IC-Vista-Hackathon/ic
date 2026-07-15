using Pronto.BillerExperience.Api.Domain;
using Pronto.BillerExperience.Contracts.V1.Onboarding;
using Pronto.BillerExperience.Contracts.V1.Research;

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
        BillerResearchResponse research,
        CancellationToken cancellationToken)
    {
        try
        {
            return await primary.GenerateAsync(biller, current, messages, research, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogFallback(logger, biller.Id, exception);
            return await fallback.GenerateAsync(biller, current, messages, research, cancellationToken);
        }
    }

    [LoggerMessage(2202, LogLevel.Error,
        "Azure AI generation failed for biller {BillerId}; using deterministic fallback")]
    private static partial void LogFallback(ILogger logger, string billerId, Exception exception);
}
