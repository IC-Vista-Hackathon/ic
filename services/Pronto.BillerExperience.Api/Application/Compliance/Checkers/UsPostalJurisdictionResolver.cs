namespace Pronto.BillerExperience.Api.Application.Compliance.Checkers;

/// <summary>
/// Deterministic mapping from a US ZIP code to its USPS state code, keyed by the ZIP's first three
/// digits (the ZCTA prefix). Not exhaustive to the last edge case, but stable and dependency-free —
/// enough for the compliance suite to resolve a biller's operating jurisdiction from
/// <c>BillerRecord.PostalCode</c>. Returns <c>null</c> when the jurisdiction cannot be established.
/// </summary>
public static class UsPostalJurisdictionResolver
{
    public static string? ResolveStateCode(string? postalCode)
    {
        if (string.IsNullOrWhiteSpace(postalCode))
        {
            return null;
        }

        var trimmed = postalCode.Trim();
        if (trimmed.Length < 3)
        {
            return null;
        }

        if (!int.TryParse(trimmed.AsSpan(0, 3), out var prefix))
        {
            return null;
        }

        foreach (var (start, end, state) in Ranges)
        {
            if (prefix >= start && prefix <= end)
            {
                return state;
            }
        }

        return null;
    }

    // Inclusive ZIP3 ranges to USPS state codes, ordered by prefix. Sourced from the standard USPS
    // ZIP-code-prefix allocation.
    private static readonly (int Start, int End, string State)[] Ranges =
    [
        (5, 5, "NY"),   // 00501/00544 Holtsville, NY
        (6, 9, "PR"),
        (10, 27, "MA"),
        (28, 29, "RI"),
        (30, 38, "NH"),
        (39, 49, "ME"),
        (50, 59, "VT"),
        (60, 69, "CT"),
        (70, 89, "NJ"),
        (100, 149, "NY"),
        (150, 196, "PA"),
        (197, 199, "DE"),
        (200, 205, "DC"),
        (206, 219, "MD"),
        (220, 246, "VA"),
        (247, 268, "WV"),
        (270, 289, "NC"),
        (290, 299, "SC"),
        (300, 319, "GA"),
        (320, 349, "FL"),
        (350, 369, "AL"),
        (370, 385, "TN"),
        (386, 397, "MS"),
        (398, 399, "GA"),
        (400, 427, "KY"),
        (430, 459, "OH"),
        (460, 479, "IN"),
        (480, 499, "MI"),
        (500, 528, "IA"),
        (530, 549, "WI"),
        (550, 567, "MN"),
        (569, 576, "SD"),
        (577, 577, "SD"),
        (580, 588, "ND"),
        (590, 599, "MT"),
        (600, 629, "IL"),
        (630, 658, "MO"),
        (660, 679, "KS"),
        (680, 693, "NE"),
        (700, 714, "LA"),
        (716, 729, "AR"),
        (730, 749, "OK"),
        (750, 799, "TX"),
        (800, 816, "CO"),
        (820, 831, "WY"),
        (832, 838, "ID"),
        (840, 847, "UT"),
        (850, 865, "AZ"),
        (870, 884, "NM"),
        (889, 898, "NV"),
        (900, 961, "CA"),
        (967, 968, "HI"),
        (969, 969, "GU"),
        (970, 979, "OR"),
        (980, 994, "WA"),
        (995, 999, "AK"),
    ];
}
