using System.Text.Json.Nodes;

namespace Pronto.Functional.Tests;

public static class JsonExtensions
{
    /// <summary>Reads a string leaf, returning null when the node is absent or JSON null.</summary>
    public static string? AsStringOrNull(this JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    /// <summary>Concatenated lowercase text of an invoice's payer-visible fields.</summary>
    public static string InvoiceText(this JsonNode? invoice) =>
        invoice is null ? string.Empty : string.Join(
            " ",
            new[]
            {
                invoice["description"].AsStringOrNull(),
                invoice["type"].AsStringOrNull(),
                invoice["note"].AsStringOrNull(),
            }.Where(value => value is not null))
        .ToLowerInvariant();
}
