using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Pronto.BillerExperience.Api.Infrastructure;
using Pronto.BillerExperience.Contracts.V1.Billing;

namespace Pronto.BillerExperience.Api.Application;

/// <summary>
/// Server-owned questionnaire state machine. The conversational model may explain or restate
/// these questions, but it cannot skip them, invent policy, or declare the profile complete.
/// </summary>
public sealed partial class BillingDiscoveryEngine(ILogger<BillingDiscoveryEngine> logger)
{
    private static readonly Regex SplitCategories = new(@"\s*(?:,|;|\band\b|&)\s*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex GracePeriod = new(@"\b(?<days>\d{1,3})\s*[- ]?day(?:s)?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex MaximumInstallments = new(@"\b(?:up to|max(?:imum)?(?: of)?)\s*(?<count>\d{1,2})\s*(?:installments?|payments?)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public BillingDiscoveryState Inspect(BillingProfile? source)
    {
        var profile = Normalize(source);
        var questions = BuildQuestions(profile);
        var current = questions.FirstOrDefault(question => IsMissing(profile, question));
        var completed = questions.Count(question => !IsMissing(profile, question));
        var missing = questions.Where(question => IsMissing(profile, question)).Select(question => question.QuestionId).ToArray();
        return new BillingDiscoveryState(
            profile,
            current,
            new BillingDiscoveryProgress(completed, questions.Count, current is null),
            missing);
    }

    public BillingDiscoveryTurn ApplyAnswer(string billerId, BillingProfile? source, string message)
    {
        using var activity = BillerExperienceTelemetry.Source.StartActivity("onboarding.answer.parse");
        var before = Inspect(source);
        activity?.SetTag("ic.biller_id", billerId);
        activity?.SetTag("ic.question_id", before.CurrentQuestion?.QuestionId);
        if (before.CurrentQuestion is null)
        {
            return new BillingDiscoveryTurn(before, false, "Your billing policy is complete. Ask me to change a specific category whenever you need to revise it.");
        }

        var updated = before.CurrentQuestion.Dimension switch
        {
            BillingDiscoveryDimension.Categories => ApplyCategories(before.Profile, message),
            BillingDiscoveryDimension.Cadence => ApplyCadence(before.Profile, before.CurrentQuestion, message),
            BillingDiscoveryDimension.StateRules => ApplyStateRule(before.Profile, before.CurrentQuestion, message),
            BillingDiscoveryDimension.PaymentTerms => ApplyPaymentTerms(before.Profile, before.CurrentQuestion, message),
            BillingDiscoveryDimension.Confirmation => ApplyConfirmation(before.Profile, message),
            _ => before.Profile
        };
        var accepted = !Equals(updated, before.Profile);
        var after = Inspect(updated);
        if (!accepted)
        {
            LogAnswerNeedsClarification(logger, billerId, before.CurrentQuestion.QuestionId, before.CurrentQuestion.Dimension.ToString());
            BillerExperienceTelemetry.DiscoveryAnswers.Add(1, new("dimension", before.CurrentQuestion.Dimension.ToString()), new("outcome", "needs_clarification"));
            activity?.SetStatus(ActivityStatusCode.Error, "needs_clarification");
            return new BillingDiscoveryTurn(after, false,
                $"I couldn't safely map that to {DescribeExpectedAnswer(before.CurrentQuestion)}. {before.CurrentQuestion.Prompt}");
        }

        LogAnswerAccepted(logger, billerId, before.CurrentQuestion.QuestionId, before.CurrentQuestion.Dimension.ToString());
        BillerExperienceTelemetry.DiscoveryAnswers.Add(1, new("dimension", before.CurrentQuestion.Dimension.ToString()), new("outcome", "accepted"));
        var reply = after.CurrentQuestion is null
            ? "Billing discovery is complete. I recorded the category-specific cadence, state rules, and payment terms."
            : $"Got it. {after.CurrentQuestion.Prompt}";
        return new BillingDiscoveryTurn(after, true, reply);
    }

    public BillingDiscoveryState Reopen(string billerId, BillingProfile? source, string questionId)
    {
        var profile = Normalize(source) with { Confirmed = false };
        var question = BuildQuestions(profile).FirstOrDefault(item => item.QuestionId == questionId)
            ?? throw new ArgumentException($"Billing discovery question '{questionId}' was not found.", nameof(questionId));
        profile = question.Dimension switch
        {
            BillingDiscoveryDimension.Categories => BillingProfile.Empty,
            BillingDiscoveryDimension.Cadence => UpdateCategory(profile, question.CategoryId!, category => category with { Cadence = null, Confirmed = false }),
            BillingDiscoveryDimension.StateRules => UpdateCategory(profile, question.CategoryId!, category => category with { StateRules = null, Confirmed = false }),
            BillingDiscoveryDimension.PaymentTerms => UpdateCategory(profile, question.CategoryId!, category => category with { PaymentTerms = null, Confirmed = false }),
            BillingDiscoveryDimension.Confirmation => profile,
            _ => profile
        };
        LogQuestionReopened(logger, billerId, questionId);
        return Inspect(profile);
    }

    private static BillingProfile ApplyCategories(BillingProfile profile, string message)
    {
        var text = message.Trim();
        if (text.StartsWith("build ", StringComparison.OrdinalIgnoreCase) ||
            !Regex.IsMatch(text, @"\b(premium|dues?|assessment|fines?|fees?|charge|bill|invoice|rent|tax|subscription|service)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return profile;

        var parenthetical = Regex.Match(text, @"(?<prefix>[^()]*(?:premium|policy)[^()]*)\((?<items>[^)]+)\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        IEnumerable<string> candidates;
        if (parenthetical.Success)
        {
            candidates = SplitCategories.Split(parenthetical.Groups["items"].Value)
                .Select(item => $"{CleanCategoryName(item)} premium");
        }
        else
        {
            text = Regex.Replace(text, @"^.*?\b(?:bill(?:ing)?(?: people)? for|charge(?: people)? for|categories? (?:are|include)|line items? (?:are|include))\b\s*[:=-]?\s*", "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            candidates = SplitCategories.Split(text).Select(CleanCategoryName);
        }

        var names = candidates.Where(IsUsefulCategoryName).Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToArray();
        if (names.Length == 0) return profile;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var categories = names.Select(name =>
        {
            var root = Slug(name);
            var id = root;
            for (var suffix = 2; !ids.Add(id); suffix++) id = $"{root}-{suffix}";
            return new BillingCategory(id, ToTitle(name));
        }).ToArray();
        return new BillingProfile("1.0", categories);
    }

    private static BillingProfile ApplyCadence(BillingProfile profile, BillingDiscoveryQuestion current, string message)
    {
        var assignments = new Dictionary<string, BillingCadence>(StringComparer.Ordinal);
        foreach (var category in profile.Categories)
        {
            var index = message.IndexOf(category.DisplayName, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                var first = category.DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
                index = message.IndexOf(first, StringComparison.OrdinalIgnoreCase);
            }
            if (index >= 0 && TryCadence(message.Substring(index, Math.Min(90, message.Length - index)), out var cadence))
                assignments[category.Id] = cadence;
        }
        if (assignments.Count == 0 && TryCadence(message, out var single)) assignments[current.CategoryId!] = single;
        if (assignments.Count == 0) return profile;
        return profile with
        {
            Confirmed = false,
            Categories = profile.Categories.Select(category => assignments.TryGetValue(category.Id, out var cadence)
                ? category with { Cadence = cadence, Confirmed = false }
                : category).ToArray()
        };
    }

    private static BillingProfile ApplyStateRule(BillingProfile profile, BillingDiscoveryQuestion current, string message)
    {
        var text = message.Trim();
        if (text.Length < 2) return profile;
        var explicitlyNone = Regex.IsMatch(text, @"\b(no|none|not applicable|does not change|no change)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var hasRuleLanguage = explicitlyNone || Regex.IsMatch(text, @"\b(late|past due|delinquent|delinquency|lapse|grace|suspend|cancel|state|after|due date)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!hasRuleLanguage) return profile;
        int? days = null;
        var grace = GracePeriod.Match(text);
        if (grace.Success) days = int.Parse(grace.Groups["days"].Value, CultureInfo.InvariantCulture);
        var resultingState = Regex.Match(text, @"\b(lapsed|lapse|delinquent|late|past due|suspended|cancelled|canceled|inactive)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) is { Success: true } state
            ? state.Value.ToLowerInvariant()
            : null;
        if (current.QuestionId.EndsWith(".resulting_state", StringComparison.Ordinal))
        {
            if (resultingState is null) return profile;
            return UpdateCategory(profile with { Confirmed = false }, current.CategoryId!, category =>
            {
                var existing = category.StateRules is { Count: > 0 } ? category.StateRules[0] : null;
                return existing is null ? category : category with
                {
                    StateRules = [existing with { ResultingState = resultingState, Description = $"{existing.Description} Resulting state: {resultingState}." }],
                    Confirmed = false
                };
            });
        }
        var description = explicitlyNone ? "No late or account-state change rule applies." : text;
        return UpdateCategory(profile with { Confirmed = false }, current.CategoryId!, category =>
            category with { StateRules = [new AccountStateRule(description, days, resultingState)], Confirmed = false });
    }

    private static BillingProfile ApplyPaymentTerms(BillingProfile profile, BillingDiscoveryQuestion current, string message)
    {
        if (current.QuestionId.EndsWith(".installment_limits", StringComparison.Ordinal))
        {
            var category = Find(profile, current.CategoryId!);
            if (category?.PaymentTerms?.Mode != SettlementMode.InstallmentsAllowed) return profile;
            var noFixedMaximum = Regex.IsMatch(message, @"\b(no (?:fixed )?(?:maximum|limit)|unlimited|case[- ]by[- ]case)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            var number = Regex.Match(message, @"\b(?<count>\d{1,2})\b", RegexOptions.CultureInvariant);
            if (!noFixedMaximum && !number.Success) return profile;
            var maximum = number.Success ? int.Parse(number.Groups["count"].Value, CultureInfo.InvariantCulture) : (int?)null;
            return UpdateCategory(profile with { Confirmed = false }, current.CategoryId!, item => item with
            {
                PaymentTerms = item.PaymentTerms! with { MaximumInstallments = maximum, LimitsConfirmed = true, Details = message.Trim() },
                Confirmed = false
            });
        }
        var assignments = new Dictionary<string, PaymentTerms>(StringComparer.Ordinal);
        var clauses = Regex.Split(message, @"[,;.]", RegexOptions.CultureInvariant);
        foreach (var clause in clauses)
        {
            if (!TryPaymentTerms(clause, out var terms)) continue;
            var matched = profile.Categories.Where(category =>
                clause.Contains(category.DisplayName, StringComparison.OrdinalIgnoreCase) ||
                category.DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Any(word => word.Length > 3 && clause.Contains(word, StringComparison.OrdinalIgnoreCase))).ToArray();
            foreach (var category in matched) assignments[category.Id] = terms;
        }
        if (assignments.Count == 0 && TryPaymentTerms(message, out var single)) assignments[current.CategoryId!] = single;
        if (assignments.Count == 0) return profile;
        return profile with
        {
            Confirmed = false,
            Categories = profile.Categories.Select(category => assignments.TryGetValue(category.Id, out var terms)
                ? category with { PaymentTerms = terms, Confirmed = false }
                : category).ToArray()
        };
    }

    private static BillingProfile ApplyConfirmation(BillingProfile profile, string message)
    {
        if (!Regex.IsMatch(message, @"\b(yes|confirm|confirmed|correct|looks good|that's right|that is right|approve)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return profile;
        return profile with
        {
            Confirmed = true,
            Categories = profile.Categories.Select(category => category with { Confirmed = true }).ToArray()
        };
    }

    private static List<BillingDiscoveryQuestion> BuildQuestions(BillingProfile profile)
    {
        var questions = new List<BillingDiscoveryQuestion>
        {
            new("billing.categories", BillingDiscoveryDimension.Categories,
                "What are you billing people for? List each line-item or billing category separately.", Sequence: 1)
        };
        var sequence = 2;
        foreach (var category in profile.Categories)
            questions.Add(new($"billing.category.{category.Id}.cadence", BillingDiscoveryDimension.Cadence,
                $"How often is {category.DisplayName} billed—monthly, quarterly, annually, one-time, ad hoc, or on another cadence?", category.Id, category.DisplayName, sequence++));
        foreach (var category in profile.Categories)
            questions.Add(new($"billing.category.{category.Id}.state_rules", BillingDiscoveryDimension.StateRules,
                $"For {category.DisplayName}, what rules decide when payment is late or the policy/account changes state? Include any grace period and resulting state. Say 'none' explicitly if no rule applies.", category.Id, category.DisplayName, sequence++));
        foreach (var category in profile.Categories.Where(NeedsResultingState))
            questions.Add(new($"billing.category.{category.Id}.resulting_state", BillingDiscoveryDimension.StateRules,
                $"You described when {category.DisplayName} changes, but not the resulting policy/account state. Does it become late, delinquent, lapsed, suspended, cancelled, or another state?", category.Id, category.DisplayName, sequence++, "state_transition_gap"));
        foreach (var category in profile.Categories)
            questions.Add(new($"billing.category.{category.Id}.payment_terms", BillingDiscoveryDimension.PaymentTerms,
                $"Can {category.DisplayName} be paid in installments, or must it be paid in full?", category.Id, category.DisplayName, sequence++));
        foreach (var category in profile.Categories.Where(category => category.PaymentTerms is { Mode: SettlementMode.InstallmentsAllowed, LimitsConfirmed: false }))
            questions.Add(new($"billing.category.{category.Id}.installment_limits", BillingDiscoveryDimension.PaymentTerms,
                $"What is the maximum number of installments for {category.DisplayName}? Say 'no fixed maximum' if it is decided case by case.", category.Id, category.DisplayName, sequence++, "missing_installment_limit"));
        if (profile.Categories.Count > 0)
            questions.Add(new("billing.confirmation", BillingDiscoveryDimension.Confirmation,
                $"Please confirm this billing policy: {Summarize(profile)}", Sequence: sequence));
        return questions;
    }

    private static bool IsMissing(BillingProfile profile, BillingDiscoveryQuestion question) => question.Dimension switch
    {
        BillingDiscoveryDimension.Categories => profile.Categories.Count == 0,
        BillingDiscoveryDimension.Cadence => Find(profile, question.CategoryId!)?.Cadence is null,
        BillingDiscoveryDimension.StateRules when question.QuestionId.EndsWith(".resulting_state", StringComparison.Ordinal) => NeedsResultingState(Find(profile, question.CategoryId!)),
        BillingDiscoveryDimension.StateRules => Find(profile, question.CategoryId!)?.StateRules is not { Count: > 0 },
        BillingDiscoveryDimension.PaymentTerms when question.QuestionId.EndsWith(".installment_limits", StringComparison.Ordinal) => Find(profile, question.CategoryId!)?.PaymentTerms is { Mode: SettlementMode.InstallmentsAllowed, LimitsConfirmed: false },
        BillingDiscoveryDimension.PaymentTerms => Find(profile, question.CategoryId!)?.PaymentTerms is null,
        BillingDiscoveryDimension.Confirmation => !profile.Confirmed,
        _ => true
    };

    private static BillingProfile Normalize(BillingProfile? source) => source is null
        ? BillingProfile.Empty
        : source with { Categories = source.Categories ?? [] };

    private static BillingCategory? Find(BillingProfile profile, string id) => profile.Categories.FirstOrDefault(category => category.Id == id);

    private static BillingProfile UpdateCategory(BillingProfile profile, string id, Func<BillingCategory, BillingCategory> update) =>
        profile with { Categories = profile.Categories.Select(category => category.Id == id ? update(category) : category).ToArray() };

    private static bool TryCadence(string text, out BillingCadence cadence)
    {
        var value = text.ToLowerInvariant();
        if (Regex.IsMatch(value, @"\b(monthly|every month)\b")) cadence = new(BillingCadenceKind.Monthly);
        else if (Regex.IsMatch(value, @"\b(quarterly|every quarter)\b")) cadence = new(BillingCadenceKind.Quarterly);
        else if (Regex.IsMatch(value, @"\b(annual|annually|yearly|every year)\b")) cadence = new(BillingCadenceKind.Annual);
        else if (Regex.IsMatch(value, @"\b(one[- ]?time|once)\b")) cadence = new(BillingCadenceKind.OneTime);
        else if (Regex.IsMatch(value, @"\b(ad[- ]?hoc|as needed|per occurrence|per notice)\b")) cadence = new(BillingCadenceKind.AdHoc);
        else { cadence = null!; return false; }
        return true;
    }

    private static bool TryPaymentTerms(string text, out PaymentTerms terms)
    {
        if (Regex.IsMatch(text, @"\b(installments?|split(?:table)?|payment plan|paid over time)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            int? maximum = null;
            var match = MaximumInstallments.Match(text);
            if (match.Success) maximum = int.Parse(match.Groups["count"].Value, CultureInfo.InvariantCulture);
            terms = new(SettlementMode.InstallmentsAllowed, maximum, text.Trim(), maximum.HasValue);
            return true;
        }
        if (Regex.IsMatch(text, @"\b(pay(?:ment)?[- ]?in[- ]?full|paid in full|full payment|in full|no installments?)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            terms = new(SettlementMode.PayInFull, Details: text.Trim(), LimitsConfirmed: true);
            return true;
        }
        terms = null!;
        return false;
    }

    private static string Summarize(BillingProfile profile) => string.Join("; ", profile.Categories.Select(category =>
        $"{category.DisplayName}: {DescribeCadence(category.Cadence)}, {DescribeStateRule(category.StateRules)}, {DescribeTerms(category.PaymentTerms)}"));

    private static string DescribeCadence(BillingCadence? cadence) => cadence?.Kind switch
    {
        BillingCadenceKind.OneTime => "one-time",
        BillingCadenceKind.AdHoc => "ad hoc",
        null => "cadence not set",
        _ => cadence.Kind.ToString().ToLowerInvariant()
    };

    private static string DescribeStateRule(IReadOnlyList<AccountStateRule>? rules) => rules is { Count: > 0 } ? rules[0].Description : "state rule not set";
    private static string DescribeTerms(PaymentTerms? terms) => terms?.Mode == SettlementMode.InstallmentsAllowed ? "installments allowed" : terms?.Mode == SettlementMode.PayInFull ? "pay in full" : "payment terms not set";
    private static string DescribeExpectedAnswer(BillingDiscoveryQuestion question) => question.Dimension switch
    {
        BillingDiscoveryDimension.Categories => "a list of billing categories",
        BillingDiscoveryDimension.Cadence => "a category-specific billing cadence",
        BillingDiscoveryDimension.StateRules => "a late-payment or state-change rule",
        BillingDiscoveryDimension.PaymentTerms => "pay-in-full or installment terms",
        BillingDiscoveryDimension.Confirmation => "an explicit confirmation",
        _ => "the required billing detail"
    };

    private static string CleanCategoryName(string value) => Regex.Replace(value.Trim().Trim('.', ':', '-', '–', '—'),
        @"^(?:we |people |customers |members |policyholders |bill |charge |for |the )+", "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Trim();
    private static bool IsUsefulCategoryName(string name) => name.Length is >= 2 and <= 80 && name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 8;
    private static string ToTitle(string name) => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name.ToLowerInvariant());
    private static string Slug(string value)
    {
        var slug = Regex.Replace(value.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
        return slug.Length == 0 ? "category" : slug;
    }

    private static bool NeedsResultingState(BillingCategory? category)
    {
        var rule = category?.StateRules is { Count: > 0 } ? category.StateRules[0] : null;
        return rule is not null &&
               !rule.Description.StartsWith("No late", StringComparison.OrdinalIgnoreCase) &&
               string.IsNullOrWhiteSpace(rule.ResultingState);
    }

    [LoggerMessage(2250, LogLevel.Information, "Billing discovery accepted answer for biller {BillerId}, question {QuestionId}, dimension {Dimension}")]
    private static partial void LogAnswerAccepted(ILogger logger, string billerId, string questionId, string dimension);

    [LoggerMessage(2251, LogLevel.Warning, "Billing discovery needs clarification for biller {BillerId}, question {QuestionId}, dimension {Dimension}")]
    private static partial void LogAnswerNeedsClarification(ILogger logger, string billerId, string questionId, string dimension);

    [LoggerMessage(2252, LogLevel.Information, "Billing discovery reopened question {QuestionId} for biller {BillerId}")]
    private static partial void LogQuestionReopened(ILogger logger, string billerId, string questionId);
}

public sealed record BillingDiscoveryState(
    BillingProfile Profile,
    BillingDiscoveryQuestion? CurrentQuestion,
    BillingDiscoveryProgress Progress,
    IReadOnlyList<string> MissingFields);

public sealed record BillingDiscoveryTurn(BillingDiscoveryState State, bool AnswerAccepted, string Reply);
