using Pronto.BillerExperience.Api.Domain;
using Pronto.BillerExperience.Contracts.V1.Onboarding;

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
        CancellationToken cancellationToken)
    {
        try
        {
            return await primary.GenerateAsync(biller, current, messages, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogFallback(logger, biller.Id, exception);
            return await fallback.GenerateAsync(biller, current, messages, cancellationToken);
        }
    }

    [LoggerMessage(2202, LogLevel.Error,
        "Azure AI generation failed for biller {BillerId}; using deterministic fallback")]
    private static partial void LogFallback(ILogger logger, string billerId, Exception exception);
}
