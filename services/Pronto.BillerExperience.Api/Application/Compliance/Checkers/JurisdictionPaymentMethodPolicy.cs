namespace Pronto.BillerExperience.Api.Application.Compliance.Checkers;

/// <summary>
/// Deterministic statement of which payment rails are available for a given operating jurisdiction.
/// This is a platform rail-availability policy (fake rails), not a legal claim: rails permitted
/// everywhere are listed once, and a jurisdiction may further restrict the set. Data-driven so the
/// policy can grow without code changes to the checker.
/// </summary>
public sealed class JurisdictionPaymentMethodPolicy
{
    private readonly IReadOnlySet<string> _globallyPermitted;
    private readonly IReadOnlyDictionary<string, IReadOnlySet<string>> _permittedByState;

    public JurisdictionPaymentMethodPolicy(
        IReadOnlySet<string> globallyPermitted,
        IReadOnlyDictionary<string, IReadOnlySet<string>> permittedByState)
    {
        _globallyPermitted = globallyPermitted;
        _permittedByState = permittedByState;
    }

    public static JurisdictionPaymentMethodPolicy Default { get; } = Create();

    /// <summary>True when the (normalized) method is available for the given USPS state code.</summary>
    public bool IsPermitted(string stateCode, string method)
    {
        var normalized = Normalize(method);
        if (_permittedByState.TryGetValue(stateCode, out var permitted))
        {
            return permitted.Contains(normalized);
        }

        return _globallyPermitted.Contains(normalized);
    }

    public static string Normalize(string method) => method.Trim().ToLowerInvariant();

    private static JurisdictionPaymentMethodPolicy Create()
    {
        var global = new HashSet<string>(StringComparer.Ordinal)
        {
            "card", "credit_card", "debit_card",
            "ach", "bank_account",
            "wallet", "digital_wallet", "apple_pay", "google_pay", "paypal",
            "paper_check", "check",
        };

        // US territories on shared fake rails: card/wallet rails only — no ACH or paper check.
        var territoryRails = (IReadOnlySet<string>)new HashSet<string>(StringComparer.Ordinal)
        {
            "card", "credit_card", "debit_card",
            "wallet", "digital_wallet", "apple_pay", "google_pay", "paypal",
        };

        var byState = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            ["PR"] = territoryRails,
            ["GU"] = territoryRails,
        };

        return new JurisdictionPaymentMethodPolicy(global, byState);
    }
}
