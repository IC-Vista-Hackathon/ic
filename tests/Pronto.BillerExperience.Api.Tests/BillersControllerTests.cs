using Pronto.BillerExperience.Api.Application;
using Pronto.BillerExperience.Api.Controllers;
using Pronto.BillerExperience.Api.Infrastructure.AI;
using Pronto.BillerExperience.Api.Infrastructure.Persistence;
using Pronto.BillerExperience.Contracts.V1.Billers;
using Pronto.BillerExperience.Contracts.V1.Deployments;
using Pronto.BillerExperience.Contracts.V1.Experiences;
using Pronto.BillerExperience.Contracts.V1.Onboarding;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pronto.Agentic.Orchestration.Execution;
using Xunit;

namespace Pronto.BillerExperience.Api.Tests;

/// <summary>Covers the HTTP surface Studio drives (SSE stream excluded — functional-test territory).</summary>
public sealed class BillersControllerTests
{
    [Fact]
    public async Task CreateReturns201WithBootstrapPayload()
    {
        var controller = Controller();

        var result = await controller.Create(Request(), CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var body = Assert.IsType<OnboardingBootstrapResponse>(created.Value);
        Assert.Equal("City of Vista", body.Biller.DisplayName);
        Assert.Equal(OnboardingSessionState.CollectingInformation, body.Session.State);
        Assert.Equal(ExperienceRevisionState.Draft, body.Draft.State);
    }

    [Fact]
    public async Task FullOnboardingRoundTripThroughController()
    {
        var controller = Controller();
        var created = await controller.Create(Request(), CancellationToken.None);
        var billerId = ((OnboardingBootstrapResponse)((CreatedAtActionResult)created.Result!).Value!)
            .Biller.BillerId;

        var biller = await controller.Get(billerId, CancellationToken.None);
        Assert.Equal(billerId, ((BillerResponse)((OkObjectResult)biller.Result!).Value!).BillerId);

        var session = await controller.GetSession(billerId, CancellationToken.None);
        Assert.IsType<OnboardingSessionResponse>(((OkObjectResult)session.Result!).Value);

        var chat = await controller.Chat(
            billerId, new SendOnboardingMessageRequest("Make the primary color green"), CancellationToken.None);
        Assert.IsType<OnboardingChatResponse>(((OkObjectResult)chat.Result!).Value);

        var draft = await controller.GetConfiguration(billerId, CancellationToken.None);
        var revision = Assert.IsType<ExperienceRevisionResponse>(((OkObjectResult)draft.Result!).Value);

        var approved = await controller.ApproveConfiguration(
            billerId, new ApproveExperienceRequest(revision.Revision, "danny"), CancellationToken.None);
        Assert.Equal(
            ExperienceRevisionState.Approved,
            ((ExperienceRevisionResponse)((OkObjectResult)approved.Result!).Value!).State);

        var published = await controller.PublishConfiguration(
            billerId, new PublishExperienceRequest(billerId, revision.Revision), CancellationToken.None);
        var accepted = Assert.IsType<AcceptedResult>(published.Result);
        var deployment = Assert.IsType<DeploymentStatusResponse>(accepted.Value);

        var status = await controller.GetDeployment(billerId, deployment.DeploymentId, CancellationToken.None);
        Assert.Equal(
            deployment.DeploymentId,
            ((DeploymentStatusResponse)((OkObjectResult)status.Result!).Value!).DeploymentId);
    }

    [Fact]
    public async Task UnknownBillerSurfacesKeyNotFound()
    {
        var controller = Controller();

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => controller.Get("missing-biller", CancellationToken.None));
    }

    [Fact]
    public async Task PurchaseEndpointAdvancesBillerStatusAndTier()
    {
        var controller = Controller();
        var created = await controller.Create(Request(), CancellationToken.None);
        var billerId = ((OnboardingBootstrapResponse)((CreatedAtActionResult)created.Result!).Value!)
            .Biller.BillerId;

        var result = await controller.AdvancePurchase(
            billerId,
            new AdvanceBillerPurchaseRequest("purchase-1", BillerTier.Isolated),
            CancellationToken.None);

        var response = Assert.IsType<BillerResponse>(((OkObjectResult)result.Result!).Value);
        Assert.Equal(BillerStatus.Purchased, response.Status);
        Assert.Equal(BillerTier.Isolated, response.Tier);
    }

    private static BillersController Controller()
    {
        var onboarding = new BillerOnboardingService(
            new InMemoryBillerExperienceRepository(),
            new DeterministicExperienceDraftGenerator(NullLogger<DeterministicExperienceDraftGenerator>.Instance),
            new OrchestrationRunner(),
            NullLogger<BillerOnboardingService>.Instance,
            invoiceSeeder: null);
        return new BillersController(
            onboarding,
            NullLogger<BillersController>.Instance,
            Options.Create(new JsonOptions()));
    }

    private static CreateBillerRequest Request() =>
        new("City of Vista", "city-of-vista", "Utility", "02110", new Uri("https://vista.example"));
}
