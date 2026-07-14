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
                },
                Ui = ApplyUiRequest(current.Definition.Ui, lastMessage),
                Preferences = ApplyPreferenceRequest(current.Definition, lastMessage)
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

    private static ExperienceUi ApplyUiRequest(ExperienceUi? current, string message)
    {
        var ui = current ?? new ExperienceUi("centered-card", new("comfortable", "rounded", "subtle"), [], []);
        if (!message.Contains("pay later", StringComparison.OrdinalIgnoreCase)) return ui;
        var action = new ExperienceAction("primary-payment-action", "Pay Later", ExperienceActionType.SchedulePayment);
        return ui with { Actions = ui.Actions.Where(item => item.Id != action.Id).Append(action).ToArray() };
    }

    private static ExperiencePreferences ApplyPreferenceRequest(BillerExperienceDefinition definition, string message)
    {
        var current = definition.Preferences ?? new ExperiencePreferences(
            true, true, true, true, ReminderChannel.Both,
            definition.EnabledPaymentCapabilities, true, true, FeeHandling.Mixed,
            new PreviewPreferences("desktop", ["payment", "history", "communication", "complex"]));

        var acceptedMethods = current.AcceptedMethods.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var capability in definition.EnabledPaymentCapabilities)
        {
            if (message.Contains($"enable {capability}", StringComparison.OrdinalIgnoreCase) ||
                message.Contains($"accept {capability}", StringComparison.OrdinalIgnoreCase))
            {
                acceptedMethods.Add(capability);
            }
            if (message.Contains($"disable {capability}", StringComparison.OrdinalIgnoreCase) ||
                message.Contains($"remove {capability}", StringComparison.OrdinalIgnoreCase))
            {
                acceptedMethods.Remove(capability);
            }
        }

        return current with
        {
            GuestCheckoutAllowed = Toggle(message, "guest checkout", current.GuestCheckoutAllowed),
            OfferAutopay = Toggle(message, "autopay", current.OfferAutopay),
            OfferPaperless = Toggle(message, "paperless", current.OfferPaperless),
            SelfServiceHistory = Toggle(message, "account history", current.SelfServiceHistory),
            SelfServiceUpdates = Toggle(message, "account updates", current.SelfServiceUpdates),
            AcceptedMethods = acceptedMethods.ToArray()
        };
    }

    private static bool Toggle(string message, string feature, bool current)
    {
        if (message.Contains($"disable {feature}", StringComparison.OrdinalIgnoreCase) ||
            message.Contains($"remove {feature}", StringComparison.OrdinalIgnoreCase) ||
            message.Contains($"no {feature}", StringComparison.OrdinalIgnoreCase)) return false;
        if (message.Contains($"enable {feature}", StringComparison.OrdinalIgnoreCase) ||
            message.Contains($"offer {feature}", StringComparison.OrdinalIgnoreCase)) return true;
        return current;
    }

    [LoggerMessage(2200, LogLevel.Error, "Deterministic draft generation failed for biller {BillerId}; trace {TraceId}")]
    private static partial void LogGenerationError(ILogger logger, string billerId, string? traceId, Exception exception);
}
