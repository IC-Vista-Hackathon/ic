namespace Pronto.ServiceDefaults;

/// <summary>
/// The convention that makes a Studio preview an <em>isolated, flagged</em> tenant rather than the
/// biller's live/published partition. A preview tenant's <c>biller_id</c> is the live biller id
/// with the <see cref="Prefix"/> marker, so:
/// <list type="bullet">
/// <item>storage stays isolated — preview data lands in its own <c>/biller_id</c> partition, never
/// mixed with a live tenant;</item>
/// <item>any service can recognize preview traffic from the id alone (no extra lookup) and flag or
/// exclude it — e.g. the Payment Service marks preview settlements so they're kept out of real
/// reporting/reconciliation.</item>
/// </list>
/// This is a routing/label convention only: it never changes money semantics. Amounts still come
/// from the invoice and fees/totals from the server-side calculator; preview-ness is just a tag
/// derived from which tenant a payment belongs to.
/// </summary>
public static class PreviewTenant
{
    /// <summary>Marker prefix identifying a preview <c>biller_id</c>. Live biller ids never carry it.</summary>
    public const string Prefix = "preview-";

    /// <summary>True when <paramref name="billerId"/> is a preview tenant id.</summary>
    public static bool IsPreview(string? billerId) =>
        !string.IsNullOrEmpty(billerId) && billerId.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>
    /// The preview tenant id for a live biller. Idempotent: passing an already-preview id returns it
    /// unchanged, so the mapping is stable no matter how many times it's applied.
    /// </summary>
    public static string ForBiller(string billerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(billerId);
        return IsPreview(billerId) ? billerId : Prefix + billerId;
    }

    /// <summary>
    /// The live biller id a preview tenant shadows. Inverse of <see cref="ForBiller"/>; returns the
    /// id unchanged when it isn't a preview id.
    /// </summary>
    public static string LiveBillerId(string previewBillerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(previewBillerId);
        return IsPreview(previewBillerId) ? previewBillerId[Prefix.Length..] : previewBillerId;
    }
}
