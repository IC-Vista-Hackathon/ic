using System.Text.Json;
using IC.BillerExperience.Contracts.V1.Deployments;
using Xunit;

namespace IC.BillerExperience.Contracts.Tests;

public sealed class DeploymentContractsTests
{
    private static readonly JsonSerializerOptions CaseInsensitive = new(JsonSerializerDefaults.Web);

    [Fact]
    public void ReadyDeploymentRoundTripsThroughJson()
    {
        var response = new DeploymentStatusResponse(
            DeploymentId: "6e1f9a3c-4b2d-48e7-95a0-8c3d7f2b6e14",
            BillerId: "3b7c1d52-88aa-4f0e-bd21-64a9a0f3c111",
            Revision: "rev-7",
            State: DeploymentState.Ready,
            PublishedUrl: new Uri("https://pay.ic.dev/plano"),
            FailureCode: null,
            FailureMessage: null,
            UpdatedAt: new DateTimeOffset(2026, 7, 14, 14, 0, 0, TimeSpan.Zero));

        var roundTripped = JsonSerializer.Deserialize<DeploymentStatusResponse>(
            JsonSerializer.Serialize(response, CaseInsensitive), CaseInsensitive);

        Assert.Equal(response, roundTripped);
    }

    [Fact]
    public void FailedDeploymentCarriesFailureDetailsAndNoUrl()
    {
        var response = new DeploymentStatusResponse(
            DeploymentId: "6e1f9a3c-4b2d-48e7-95a0-8c3d7f2b6e14",
            BillerId: "3b7c1d52-88aa-4f0e-bd21-64a9a0f3c111",
            Revision: "rev-7",
            State: DeploymentState.Failed,
            PublishedUrl: null,
            FailureCode: "readiness_timeout",
            FailureMessage: "Site did not become ready within 10 minutes.",
            UpdatedAt: new DateTimeOffset(2026, 7, 14, 14, 0, 0, TimeSpan.Zero));

        var roundTripped = JsonSerializer.Deserialize<DeploymentStatusResponse>(
            JsonSerializer.Serialize(response, CaseInsensitive), CaseInsensitive);

        Assert.Equal(response, roundTripped);
        Assert.Null(roundTripped!.PublishedUrl);
        Assert.Equal("readiness_timeout", roundTripped.FailureCode);
    }

    [Fact]
    public void PublishExperienceRequestRoundTripsThroughJson()
    {
        var request = new PublishExperienceRequest(
            BillerId: "3b7c1d52-88aa-4f0e-bd21-64a9a0f3c111",
            Revision: "rev-7");

        var roundTripped = JsonSerializer.Deserialize<PublishExperienceRequest>(
            JsonSerializer.Serialize(request, CaseInsensitive), CaseInsensitive);

        Assert.Equal(request, roundTripped);
    }
}
