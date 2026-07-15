using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pronto.Persistence.Cosmos;

/// <summary>
/// System.Text.Json options for documents stored in Cosmos. Matches the Pronto wire
/// format (snake_case properties, lowercase string enums) so persisted documents
/// read identically to the HTTP contracts — see libraries/Pronto.ServiceDefaults.
/// </summary>
public static class CosmosJson
{
    public static JsonSerializerOptions CreateOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,

        // Deliberately no DictionaryKeyPolicy: System.Text.Json applies it on write but
        // never reverses it on read, so any non-snake_case key would fail to round-trip.
        // Dictionary keys are data and are persisted verbatim.
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false),
        },
    };
}
