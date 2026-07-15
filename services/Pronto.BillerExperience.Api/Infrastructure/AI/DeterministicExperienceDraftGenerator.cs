using System.Diagnostics;
using System.Text.RegularExpressions;
using Pronto.BillerExperience.Api.Domain;
using Pronto.BillerExperience.Api.Infrastructure;
using Pronto.BillerExperience.Contracts.V1.Experiences;
using Pronto.BillerExperience.Contracts.V1.Onboarding;
using Pronto.BillerExperience.Contracts.V1.Research;

namespace Pronto.BillerExperience.Api.Infrastructure.AI;

public sealed partial class DeterministicExperienceDraftGenerator(
    ILogger<DeterministicExperienceDraftGenerator> logger) : IExperienceDraftGenerator
{
    public string Provider => "Deterministic";

    public ValueTask<DraftGenerationResult> GenerateAsync(
        BillerRecord biller,
        ExperienceRecord current,
        IReadOnlyList<OnboardingChatMessage> messages,
        BillerResearchResponse research,
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
            var primaryColor = ResolvePrimaryColor(lastMessage, current.Definition.Brand.PrimaryColor);
            // Only touch fields the request actually asks for — the deterministic fallback must not
            // silently overwrite the heading (or anything else) with a default, or the Studio's
            // "proposed revision" summary ends up describing changes that were never requested.
            var definition = current.Definition with
            {
                Brand = current.Definition.Brand with { PrimaryColor = primaryColor },
                Content = current.Definition.Content with
                {
                    Heading = ResolveHeading(lastMessage, current.Definition.Content.Heading)
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
                findings,
                GenerationModes.Deterministic);
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

    // Accessible, WCAG-AA-against-white brand hexes for the color names a biller is likely to type.
    // The design flow's real model handles nuance; this keeps the deterministic fallback usable
    // ("change the primary color to red") instead of silently ignoring anything but a hex code.
    private static readonly Dictionary<string, string> NamedColors =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["red"] = "#c1121f", ["crimson"] = "#a4133c", ["orange"] = "#b45309",
            ["amber"] = "#b45309", ["yellow"] = "#8a6d00", ["gold"] = "#8a6d00",
            ["green"] = "#197d00", ["emerald"] = "#0f766e", ["teal"] = "#0f766e",
            ["cyan"] = "#0e7490", ["blue"] = "#1d4ed8", ["navy"] = "#1e3a8a",
            ["indigo"] = "#4338ca", ["purple"] = "#6d28d9", ["violet"] = "#6d28d9",
            ["magenta"] = "#a21caf", ["pink"] = "#be185d", ["brown"] = "#7c4a1e",
            ["gray"] = "#4b5563", ["grey"] = "#4b5563", ["slate"] = "#334155",
            ["black"] = "#1c1c1c",
        };

    private static string ResolvePrimaryColor(string message, string current)
    {
        // An explicit hex always wins over a color name.
        if (HexColorRegex().Match(message) is { Success: true } hex) return hex.Value;

        // "from blue to red" -> the target is the color after "to"; otherwise the last color named.
        string? target = null;
        foreach (Match token in NamedColorRegex().Matches(message))
        {
            var name = token.Groups["name"].Value;
            if (!NamedColors.ContainsKey(name)) continue;
            target = name;
            if (message.AsSpan(0, token.Index).TrimEnd().EndsWith("to", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }
        }

        return target is not null ? NamedColors[target] : current;
    }

    [GeneratedRegex(@"\b(?<name>[a-zA-Z]+)\b", RegexOptions.CultureInvariant)]
    private static partial Regex NamedColorRegex();

    // Honors "change the heading to X" / "make the heading say X" / "heading: X" and returns the
    // current heading untouched when the request doesn't mention one, so we never fabricate a change.
    private static string ResolveHeading(string message, string current)
    {
        var match = HeadingRequestRegex().Match(message);
        if (!match.Success) return current;
        var text = CleanRequestedText(match.Groups["text"].Value);
        return string.IsNullOrWhiteSpace(text) ? current : text;
    }

    [GeneratedRegex(
        @"\b(?:heading|headline|title|header)\b\s*(?:should\s+)?(?:say|says|read|reads|to\s+say|to\s+read|to\s+be|to|be|is|reads?\s+as|[:=-])\s*(?<text>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex HeadingRequestRegex();

    private static ExperienceUi ApplyUiRequest(ExperienceUi? current, string message)
    {
        var ui = current ?? new ExperienceUi("centered-card", new("comfortable", "rounded", "subtle"), [], []);

        // "pay later" is an explicit intent — it also flips the primary action to a scheduled payment,
        // so it's handled before the generic label rename below.
        if (message.Contains("pay later", StringComparison.OrdinalIgnoreCase))
        {
            var action = new ExperienceAction("primary-payment-action", "Pay Later", ExperienceActionType.SchedulePayment);
            return ui with { Actions = ui.Actions.Where(item => item.Id != action.Id).Append(action).ToArray() };
        }

        // Generic "change the button/primary action to X" — rename the primary call-to-action label
        // without changing its underlying action type.
        var label = ResolvePrimaryActionLabel(message);
        if (label is null) return ui;
        if (ui.Actions.Count > 0)
        {
            var renamed = ui.Actions[0] with { Label = label };
            return ui with { Actions = ui.Actions.Skip(1).Prepend(renamed).ToArray() };
        }
        return ui with { Actions = [new ExperienceAction("primary-payment-action", label, ExperienceActionType.StartPayment)] };
    }

    // Honors "change the button to X" / "make the primary action say X" / "call to action: X".
    private static string? ResolvePrimaryActionLabel(string message)
    {
        var match = ActionLabelRequestRegex().Match(message);
        if (!match.Success) return null;
        var text = CleanRequestedText(match.Groups["text"].Value);
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    [GeneratedRegex(
        @"\b(?:primary\s+action|call\s*to\s*action|cta|button|primary\s+button|action\s+label)\b\s*(?:label\s+)?(?:should\s+)?(?:say|says|read|reads|to\s+say|to\s+read|to\s+be|to|be|is|[:=-])\s*(?<text>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex ActionLabelRequestRegex();

    // Trims a captured request phrase down to just the requested text: strips wrapping quotes,
    // stops at sentence terminators, and drops a trailing "and <do something else>" clause so a
    // compound request ("... and change the color to purple") doesn't leak into the value.
    private static string CleanRequestedText(string raw)
    {
        var text = raw.Trim();
        var terminator = text.IndexOfAny(['.', ';', '\n', '\r']);
        if (terminator >= 0) text = text[..terminator];

        var clause = TrailingClauseRegex().Match(text);
        if (clause.Success) text = text[..clause.Index];

        text = text.Trim().Trim('"', '\'', '“', '”', '‘', '’').Trim();
        return text;
    }

    [GeneratedRegex(
        @"\s*,?\s+\b(?:and|but|then|also|plus)\b\s+(?:also\s+|please\s+)?(?:change|make|set|update|use|switch|turn|enable|disable|add|remove|keep)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TrailingClauseRegex();

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
