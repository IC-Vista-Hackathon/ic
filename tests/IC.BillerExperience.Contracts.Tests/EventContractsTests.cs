using System.Text.Json;
using IC.BillerExperience.Contracts.V1.Events;
using Xunit;

namespace IC.BillerExperience.Contracts.Tests;

public sealed class EventContractsTests
{
    private static readonly JsonSerializerOptions CaseInsensitive = new(JsonSerializerDefaults.Web);

    private static readonly DateTimeOffset OccurredAt =
        new(2026, 7, 14, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ExperienceApprovedRoundTripsThroughJson()
    {
        var evt = new ExperienceApproved(
            EventId: "e1a7c3f9-5d2b-4680-9e4c-7b1a8d3f5c02",
            BillerId: "3b7c1d52-88aa-4f0e-bd21-64a9a0f3c111",
            Revision: "rev-7",
            ApprovedBy: "ddominguez",
            OccurredAt: OccurredAt);

        var roundTripped = JsonSerializer.Deserialize<ExperienceApproved>(
            JsonSerializer.Serialize(evt, CaseInsensitive), CaseInsensitive);

        Assert.Equal(evt, roundTripped);
    }

    [Fact]
    public void ExperiencePublishedRoundTripsThroughJson()
    {
        var evt = new ExperiencePublished(
            EventId: "e1a7c3f9-5d2b-4680-9e4c-7b1a8d3f5c02",
            BillerId: "3b7c1d52-88aa-4f0e-bd21-64a9a0f3c111",
            Revision: "rev-7",
            PublishedUrl: new Uri("https://pay.ic.dev/plano"),
            OccurredAt: OccurredAt);

        var roundTripped = JsonSerializer.Deserialize<ExperiencePublished>(
            JsonSerializer.Serialize(evt, CaseInsensitive), CaseInsensitive);

        Assert.Equal(evt, roundTripped);
    }

    [Fact]
    public void ExperiencePublishFailedRoundTripsThroughJson()
    {
        var evt = new ExperiencePublishFailed(
            EventId: "e1a7c3f9-5d2b-4680-9e4c-7b1a8d3f5c02",
            BillerId: "3b7c1d52-88aa-4f0e-bd21-64a9a0f3c111",
            Revision: "rev-7",
            FailureCode: "readiness_timeout",
            OccurredAt: OccurredAt);

        var roundTripped = JsonSerializer.Deserialize<ExperiencePublishFailed>(
            JsonSerializer.Serialize(evt, CaseInsensitive), CaseInsensitive);

        Assert.Equal(evt, roundTripped);
    }

    [Fact]
    public void EveryEventExposesTheSharedEnvelopeFields()
    {
        var evt = new PublishingRequested(
            EventId: "e1a7c3f9-5d2b-4680-9e4c-7b1a8d3f5c02",
            BillerId: "3b7c1d52-88aa-4f0e-bd21-64a9a0f3c111",
            Revision: "rev-7",
            OccurredAt: OccurredAt);

        var envelope = Assert.IsAssignableFrom<IBillerExperienceEvent>(evt);
        Assert.Equal("e1a7c3f9-5d2b-4680-9e4c-7b1a8d3f5c02", envelope.EventId);
        Assert.Equal("3b7c1d52-88aa-4f0e-bd21-64a9a0f3c111", envelope.BillerId);
        Assert.Equal(OccurredAt, envelope.OccurredAt);
    }
}
