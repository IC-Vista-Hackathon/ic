using System.Text.Json;
using Microsoft.Azure.Cosmos;

namespace IC.Persistence.Cosmos;

/// <summary>
/// A <see cref="CosmosSerializer"/> backed by System.Text.Json. The Cosmos SDK
/// defaults to Newtonsoft.Json; this swaps in STJ so documents honour the IC wire
/// attributes (JsonPropertyName, JsonStringEnumMemberName) and snake_case policy,
/// keeping persisted shapes identical to the HTTP contracts.
/// </summary>
public sealed class CosmosSystemTextJsonSerializer : CosmosSerializer
{
    private readonly JsonSerializerOptions options;

    public CosmosSystemTextJsonSerializer(JsonSerializerOptions options) => this.options = options;

    public override T FromStream<T>(Stream stream)
    {
        using (stream)
        {
            if (stream.CanSeek && stream.Length == 0)
            {
                return default!;
            }

            // The SDK passes some payloads (e.g. raw feed responses) through as Stream.
            if (typeof(Stream).IsAssignableFrom(typeof(T)))
            {
                return (T)(object)stream;
            }

            return JsonSerializer.Deserialize<T>(stream, options)!;
        }
    }

    public override Stream ToStream<T>(T input)
    {
        var stream = new MemoryStream();
        JsonSerializer.Serialize(stream, input, options);
        stream.Position = 0;
        return stream;
    }
}
