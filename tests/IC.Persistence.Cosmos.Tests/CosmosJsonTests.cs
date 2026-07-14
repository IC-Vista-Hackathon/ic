using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace IC.Persistence.Cosmos.Tests;

public sealed class CosmosJsonTests
{
    private enum SampleStatus
    {
        Due,
        AlreadyPaid,
    }

    private sealed record SampleDocument(
        string BillerId,
        int AmountCents,
        SampleStatus Status,
        string? OptionalNote);

    [Fact]
    public void PropertiesSerializeAsSnakeCase()
    {
        var json = JsonSerializer.Serialize(
            new SampleDocument("b-1", 8420, SampleStatus.Due, "note"), CosmosJson.CreateOptions());

        Assert.Contains("\"biller_id\"", json, StringComparison.Ordinal);
        Assert.Contains("\"amount_cents\"", json, StringComparison.Ordinal);
        Assert.Contains("\"optional_note\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void EnumsSerializeAsSnakeCaseStrings()
    {
        var json = JsonSerializer.Serialize(
            new SampleDocument("b-1", 1, SampleStatus.AlreadyPaid, null), CosmosJson.CreateOptions());

        Assert.Contains("\"already_paid\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void NullsAreOmittedFromDocuments()
    {
        var json = JsonSerializer.Serialize(
            new SampleDocument("b-1", 1, SampleStatus.Due, OptionalNote: null), CosmosJson.CreateOptions());

        Assert.DoesNotContain("optional_note", json, StringComparison.Ordinal);
    }

    [Fact]
    public void IntegerEnumTokensAreRejected()
    {
        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize<SampleDocument>(
            """{"biller_id":"b-1","amount_cents":1,"status":1}""", CosmosJson.CreateOptions()));
    }

    [Fact]
    public void DocumentsRoundTrip()
    {
        var options = CosmosJson.CreateOptions();
        var document = new SampleDocument("b-1", 8420, SampleStatus.AlreadyPaid, "hello");

        var roundTripped = JsonSerializer.Deserialize<SampleDocument>(
            JsonSerializer.Serialize(document, options), options);

        Assert.Equal(document, roundTripped);
    }
}
