using System.Text.Json;
using System.Text.Json.Serialization;
using Pronto.Agentic.Orchestration.Execution;
using Pronto.BillerExperience.Api.Application;
using Pronto.BillerExperience.Api.Domain;
using Pronto.BillerExperience.Api.Infrastructure.AI;
using Pronto.BillerExperience.Api.Infrastructure.Persistence;
using Pronto.BillerExperience.Api.Infrastructure.SupportingServices;
using Pronto.BillerExperience.Contracts.V1.Billers;
using Pronto.BillerExperience.Contracts.V1.Experiences;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using Xunit;

namespace Pronto.BillerExperience.Api.Tests;

public sealed class BillerOnboardingRegressionTests
{
    [Fact]
    public async Task ApprovedRevisionCannotBeChangedThroughChat()
    {
        var repository = new InMemoryBillerExperienceRepository();
        var service = CreateService(repository);
        var created = await service.CreateAsync(Request(), CancellationToken.None);
        var chat = await service.SendMessageAsync(
            created.Biller.BillerId,
            new("Ready for approval"),
            CancellationToken.None);
        var approved = await service.ApproveAsync(
            created.Biller.BillerId,
            new(chat.Draft!.Revision, "reviewer"),
            CancellationToken.None);

        await Assert.ThrowsAsync<ArgumentException>(() => service.SendMessageAsync(
            created.Biller.BillerId,
            new("Change the primary color to red"),
            CancellationToken.None).AsTask());

        var unchanged = await service.GetDraftAsync(created.Biller.BillerId, CancellationToken.None);
        Assert.Equal(ExperienceRevisionState.Approved, unchanged.State);
        Assert.Equal(approved.ETag, unchanged.ETag);
        Assert.Equal(approved.Definition, unchanged.Definition);
    }

    [Fact]
    public async Task PublishedRevisionCannotBeChangedThroughChat()
    {
        var repository = new InMemoryBillerExperienceRepository();
        var service = CreateService(repository);
        var created = await service.CreateAsync(Request(), CancellationToken.None);
        var current = await repository.GetLatestExperienceAsync(
            created.Biller.BillerId,
            CancellationToken.None);
        var published = await repository.SaveExperienceAsync(
            current! with { State = ExperienceRevisionState.Published },
            current.ETag,
            CancellationToken.None);

        await Assert.ThrowsAsync<ArgumentException>(() => service.SendMessageAsync(
            created.Biller.BillerId,
            new("Change the primary color to red"),
            CancellationToken.None).AsTask());

        var unchanged = await service.GetDraftAsync(created.Biller.BillerId, CancellationToken.None);
        Assert.Equal(ExperienceRevisionState.Published, unchanged.State);
        Assert.Equal(published.ETag, unchanged.ETag);
        Assert.Equal(published.Definition, unchanged.Definition);
    }

    [Fact]
    public async Task ExperienceRecordMapsDocumentETag()
    {
        var repository = new InMemoryBillerExperienceRepository();
        var created = await CreateService(repository).CreateAsync(Request(), CancellationToken.None);
        var record = await repository.GetLatestExperienceAsync(
            created.Biller.BillerId,
            CancellationToken.None);

        var json = JsonConvert.SerializeObject(record);
        var deserialized = JsonConvert.DeserializeObject<ExperienceRecord>(json);

        Assert.Contains("\"_etag\"", json, StringComparison.Ordinal);
        Assert.Equal(record!.ETag, deserialized!.ETag);
    }

    [Fact]
    public async Task FailedInvoiceSeedRemovesPartialCreationAndAllowsSlugReuse()
    {
        var repository = new InMemoryBillerExperienceRepository();
        var seeder = new FailingInvoiceSeeder();
        var service = CreateService(repository, seeder);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CreateAsync(Request(), CancellationToken.None).AsTask());

        Assert.Null(await repository.GetBillerAsync(seeder.BillerId!, CancellationToken.None));
        Assert.Null(await repository.GetLatestExperienceAsync(seeder.BillerId!, CancellationToken.None));
        Assert.Null(await repository.GetRunAsync(seeder.BillerId!, "onboarding", CancellationToken.None));
        Assert.Null(await repository.GetAgentContextAsync(
            seeder.BillerId!,
            "onboarding",
            CancellationToken.None));

        var retried = await CreateService(repository).CreateAsync(Request(), CancellationToken.None);
        Assert.Equal(Request().Slug, retried.Biller.Slug);
    }

    [Fact]
    public async Task UndefinedActionTypeBlocksApproval()
    {
        var repository = new InMemoryBillerExperienceRepository();
        var service = CreateService(repository);
        var created = await service.CreateAsync(Request(), CancellationToken.None);
        var invalidDefinition = created.Draft.Definition with
        {
            Ui = created.Draft.Definition.Ui! with
            {
                Actions =
                [
                    new ExperienceAction(
                        "unsupported",
                        "Unsupported",
                        (ExperienceActionType)999)
                ]
            }
        };

        var updated = await service.UpdateDraftAsync(
            created.Biller.BillerId,
            new(invalidDefinition, created.Draft.ETag),
            CancellationToken.None);

        Assert.Contains(updated.Findings!, finding => finding.Code == "ACTION_TYPE_INVALID");
        var exception = await Assert.ThrowsAsync<ExperienceValidationException>(
            () => service.ApproveAsync(
                created.Biller.BillerId,
                new(updated.Revision, "reviewer"),
                CancellationToken.None).AsTask());
        Assert.Contains(exception.Findings, finding => finding.Code == "ACTION_TYPE_INVALID");
    }

    [Fact]
    public void NumericActionTypeIsRejectedByJsonContract()
    {
        Assert.Throws<System.Text.Json.JsonException>(() =>
            System.Text.Json.JsonSerializer.Deserialize<ExperienceAction>(
                """{"id":"bad","label":"Bad","action":999,"variant":"primary"}""",
                ApiJsonOptions()));
    }

    private static JsonSerializerOptions ApiJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower
        };
        options.Converters.Add(
            new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false));
        return options;
    }

    private static BillerOnboardingService CreateService(
        IBillerExperienceRepository repository,
        IInvoiceSeeder? seeder = null) =>
        new(
            repository,
            new DeterministicExperienceDraftGenerator(
                NullLogger<DeterministicExperienceDraftGenerator>.Instance),
            new OrchestrationRunner(),
            NullLogger<BillerOnboardingService>.Instance,
            agentContextService: new AgentContextService(
                repository,
                NullLogger<AgentContextService>.Instance),
            invoiceSeeder: seeder);

    private static CreateBillerRequest Request() =>
        new(
            "City of Vista",
            "city-of-vista",
            "Utility",
            "02110",
            new Uri("https://vista.example"));

    private sealed class FailingInvoiceSeeder : IInvoiceSeeder
    {
        public string? BillerId { get; private set; }

        public ValueTask SeedAsync(SeedBillerContext biller, CancellationToken cancellationToken, bool replace = false)
        {
            BillerId = biller.BillerId;
            return ValueTask.FromException(new InvalidOperationException("seed failed"));
        }
    }
}
