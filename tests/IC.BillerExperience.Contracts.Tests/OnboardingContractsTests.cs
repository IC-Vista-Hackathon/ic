using System.Text.Json;
using IC.BillerExperience.Contracts.V1.Onboarding;
using Xunit;

namespace IC.BillerExperience.Contracts.Tests;

public sealed class OnboardingContractsTests
{
    private static readonly JsonSerializerOptions CaseInsensitive = new(JsonSerializerDefaults.Web);

    [Fact]
    public void OnboardingSessionResponseRoundTripsThroughJson()
    {
        var response = new OnboardingSessionResponse(
            SessionId: "b8d4f2a6-9c1e-4573-a0b8-5e7d3c9f1a26",
            BillerId: "3b7c1d52-88aa-4f0e-bd21-64a9a0f3c111",
            State: OnboardingSessionState.CollectingInformation,
            MissingFields: ["brand.primaryColor", "support.email"],
            UpdatedAt: new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero));

        var roundTripped = JsonSerializer.Deserialize<OnboardingSessionResponse>(
            JsonSerializer.Serialize(response, CaseInsensitive), CaseInsensitive);

        // OnboardingSessionResponse holds a collection, so record equality is reference-based — compare fields.
        Assert.NotNull(roundTripped);
        Assert.Equal(response.SessionId, roundTripped.SessionId);
        Assert.Equal(response.BillerId, roundTripped.BillerId);
        Assert.Equal(response.State, roundTripped.State);
        Assert.Equal(response.MissingFields, roundTripped.MissingFields);
        Assert.Equal(response.UpdatedAt, roundTripped.UpdatedAt);
    }

    [Fact]
    public void StartOnboardingRequestDefaultsToNewBiller()
    {
        var request = new StartOnboardingRequest();

        Assert.Null(request.BillerId);
    }

    [Fact]
    public void StartOnboardingRequestDeserializesEmptyBodyAsNewBiller()
    {
        var request = JsonSerializer.Deserialize<StartOnboardingRequest>("{}", CaseInsensitive);

        Assert.NotNull(request);
        Assert.Null(request.BillerId);
    }
}
