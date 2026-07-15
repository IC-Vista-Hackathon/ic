using Xunit;

namespace Pronto.Persistence.Cosmos.Tests;

public sealed class CosmosSystemTextJsonSerializerTests
{
    private sealed record Doc(string BillerId, int AmountCents);

    private static CosmosSystemTextJsonSerializer Serializer() => new(CosmosJson.CreateOptions());

    [Fact]
    public void ToStreamThenFromStreamRoundTrips()
    {
        var serializer = Serializer();
        var doc = new Doc("b-1", 8420);

        using var stream = serializer.ToStream(doc);
        var roundTripped = serializer.FromStream<Doc>(stream);

        Assert.Equal(doc, roundTripped);
    }

    [Fact]
    public void ToStreamWritesSnakeCase()
    {
        using var stream = Serializer().ToStream(new Doc("b-1", 1));
        using var reader = new StreamReader(stream);

        Assert.Contains("\"biller_id\"", reader.ReadToEnd(), StringComparison.Ordinal);
    }

    [Fact]
    public void FromStreamPassesRawStreamsThroughUndisposed()
    {
        using var stream = new MemoryStream([1, 2, 3]);

        var result = Serializer().FromStream<Stream>(stream);

        Assert.Same(stream, result);
        Assert.True(stream.CanRead); // not disposed
    }

    [Fact]
    public void FromStreamReturnsDefaultForEmptyStream()
    {
        var result = Serializer().FromStream<Doc>(new MemoryStream());

        Assert.Null(result);
    }

    [Fact]
    public void FromStreamDisposesTheSourceStream()
    {
        var serializer = Serializer();
        var stream = serializer.ToStream(new Doc("b-1", 1));

        _ = serializer.FromStream<Doc>(stream);

        Assert.False(stream.CanRead);
    }
}
