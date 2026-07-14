using System.Diagnostics;
using System.Text.RegularExpressions;
using IC.BillerExperience.Api.Domain;
using IC.BillerExperience.Api.Infrastructure;
using IC.BillerExperience.Contracts.V1.Experiences;
using IC.BillerExperience.Contracts.V1.Onboarding;

namespace IC.BillerExperience.Api.Infrastructure.AI;

public sealed partial class DeterministicExperienceDraftGenerator(
    ILogger<DeterministicExperienceDraftGenerator> logger) : IExperienceDraftGenerator
{
    public string Provider => "Deterministic";

    public ValueTask<DraftGenerationResult> GenerateAsync(
        BillerRecord biller,
        ExperienceRecord current,
        IReadOnlyList<OnboardingChatMessage> messages,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var startedAt = Stopwatch.GetTimestamp();
        using var activity = BillerExperienceTelemetry.Source.StartActivity("model:deterministic-draft");
        activity?.SetTag("gen_ai.system", "deterministic");
        activity?.SetTag("ic.biller_id", biller.Id);
        try
        {
            var lastMessage = messages.LastOrDefault(message => message.Role == "user")?.Content ?? string.Empty;
            var primaryColor = HexColorRegex().Match(lastMessage) is { Success: true } color
                ? color.Value
                : current.Definition.Brand.PrimaryColor;
            var definition = current.Definition with
            {
                Brand = current.Definition.Brand with { PrimaryColor = primaryColor },
                Content = current.Definition.Content with
                {
                    Heading = $"Pay your {biller.BillType.ToLowerInvariant()} bill",
                    Introduction = string.IsNullOrWhiteSpace(lastMessage)
                        ? current.Definition.Content.Introduction
                        : $"A simple, secure payment experience for {biller.Name}."
                }
            };
            var findings = new[]
            {
                new ComplianceFinding(
                    "COMPLIANCE_REVIEW_REQUIRED",
                    $"Payment disclosures for postal code {biller.PostalCode} require biller review before publication.",
                    ComplianceFindingSeverity.Warning)
            };
            var result = new DraftGenerationResult(
                "I updated the live preview. Review the brand, payment methods, legal links, and compliance guidance before approving it.",
                definition,
                Array.Empty<string>(),
                findings);
            BillerExperienceTelemetry.ModelCalls.Add(1, new("provider", Provider), new("outcome", "success"));
            return ValueTask.FromResult(result);
        }
        catch (Exception exception)
        {
            LogGenerationError(logger, biller.Id, activity?.TraceId.ToString(), exception);
            BillerExperienceTelemetry.ModelCalls.Add(1, new("provider", Provider), new("outcome", "error"));
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            throw;
        }
        finally
        {
            BillerExperienceTelemetry.ModelDuration.Record(
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
                new KeyValuePair<string, object?>("provider", Provider));
        }
    }

    [GeneratedRegex("#[0-9a-fA-F]{6}", RegexOptions.CultureInvariant)]
    private static partial Regex HexColorRegex();

    [LoggerMessage(2200, LogLevel.Error, "Deterministic draft generation failed for biller {BillerId}; trace {TraceId}")]
    private static partial void LogGenerationError(ILogger logger, string billerId, string? traceId, Exception exception);
}
