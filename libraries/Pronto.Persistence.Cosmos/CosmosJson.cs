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
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false),
        },
    };
}
