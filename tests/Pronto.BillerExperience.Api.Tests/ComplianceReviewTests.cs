using System.Text.Json;
using Pronto.BillerExperience.Api.Application.Compliance;
using Pronto.BillerExperience.Api.Configuration;
using Pronto.BillerExperience.Api.Domain;
using Pronto.BillerExperience.Api.Infrastructure.Compliance;
using Pronto.BillerExperience.Api.Infrastructure.Research;
using Pronto.BillerExperience.Contracts.V1.Billers;
using Pronto.BillerExperience.Contracts.V1.Experiences;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Pronto.BillerExperience.Api.Tests;

public sealed class ComplianceReviewTests
{
    private static readonly string[] SourceUrls = ["https://malegislature.gov/example"];

    [Fact]
    public void DeterministicPolicyMapsBlockingFindingsToExactFields()
    {
        var engine = new CompliancePolicyEngine(Options(false));
        var definition = Definition() with
        {
            Content = Definition().Content with
            {
                PrivacyPolicyUrl = new Uri("http://example.test/privacy")
            },
            Preferences = Definition().Preferences! with
            {
                FeeHandling = FeeHandling.Undecided,
                AcceptedMethods = ["cash"],
                OfferAutopay = true
            }
        };

        var findings = engine.Evaluate(definition);

        Assert.Contains(findings, finding =>
            finding.Code == "PRIVACY_POLICY_HTTPS_REQUIRED" &&
            finding.FieldPath == "content.privacy_policy_url" &&
            finding.PolicyVersion == "test-policy");
        Assert.Contains(findings, finding =>
            finding.Code == "FEE_POLICY_REQUIRED" &&
            finding.FieldPath == "preferences.fee_handling");
        Assert.Contains(findings, finding =>
            finding.Code == "PAYMENT_METHOD_UNSUPPORTED" &&
            finding.FieldPath == "preferences.accepted_methods");
        Assert.Contains(findings, finding =>
            finding.Code == "AUTOPAY_METHOD_REQUIRED" &&
            finding.FieldPath == "preferences.offer_autopay");
        Assert.All(findings, finding => Assert.False(finding.RequiresReview));
    }

    [Fact]
    public async Task RequiredKnowledgeReviewFailsClosedWhenReviewerIsUnavailable()
    {
        var service = new ComplianceReviewService(
            new CompliancePolicyEngine(Options(true)),
            Options(true),
            NullLogger<ComplianceReviewService>.Instance);

        var findings = await service.ReviewAsync(
            Biller(),
            Definition(),
            ComplianceReviewStage.Publish,
            CancellationToken.None);

        var unavailable = Assert.Single(findings, finding => finding.Code == "COMPLIANCE_KNOWLEDGE_UNAVAILABLE");
        Assert.Equal(ComplianceFindingSeverity.Blocking, unavailable.Severity);
    }

    [Fact]
    public async Task FoundryReviewTreatsUnconfirmedLawAsAdvisoryAndPreservesSource()
    {
        var output = JsonSerializer.Serialize(new
        {
            status = "needs_review",
            summary = "The state material is marked Not confirmed and requires counsel verification.",
            findings = new[]
            {
                new
                {
                    code = "STATE_SURCHARGE_NOT_CONFIRMED",
                    message = "Verify the cited state surcharge summary with counsel before relying on it.",
                    fieldPath = "preferences.fee_handling",
                    jurisdiction = "Massachusetts",
                    sources = SourceUrls,
                    requiresReview = true
                }
            },
            sources = new[]
            {
                new { url = "https://malegislature.gov/example", title = "Massachusetts source" }
            }
        });
        var gateway = new RecordingFoundryGateway(new FoundryAgentOutput(output, []));
        var reviewer = new FoundryComplianceKnowledgeReviewer(
            gateway,
            Options(true),
            NullLogger<FoundryComplianceKnowledgeReviewer>.Instance);

        var review = await reviewer.ReviewAsync(
            Biller(),
            Definition(),
            [],
            ComplianceReviewStage.Publish,
            CancellationToken.None);

        Assert.Equal(ComplianceKnowledgeReviewStatus.NeedsReview, review.Status);
        var finding = Assert.Single(review.Findings, item => item.Code == "STATE_SURCHARGE_NOT_CONFIRMED");
        Assert.Equal(ComplianceFindingSeverity.Warning, finding.Severity);
        Assert.Equal("preferences.fee_handling", finding.FieldPath);
        Assert.Equal("Massachusetts", finding.Jurisdiction);
        Assert.Equal(new Uri("https://malegislature.gov/example"), Assert.Single(finding.Sources!));
        Assert.Contains("untrusted evidence", gateway.Prompt, StringComparison.Ordinal);
        Assert.Contains("missing-context finding", gateway.Prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FoundryReviewRejectsFindingsWithoutEvidence()
    {
        var output = """
            {
              "status":"completed",
              "summary":"No source was supplied.",
              "findings":[{
                "code":"UNSOURCED",
                "message":"Do something.",
                "fieldPath":"preferences.fee_handling",
                "jurisdiction":"federal",
                "sources":[],
                "requiresReview":true
              }],
              "sources":[]
            }
            """;
        var reviewer = new FoundryComplianceKnowledgeReviewer(
            new RecordingFoundryGateway(new FoundryAgentOutput(output, [])),
            Options(true),
            NullLogger<FoundryComplianceKnowledgeReviewer>.Instance);

        var review = await reviewer.ReviewAsync(
            Biller(),
            Definition(),
            [],
            ComplianceReviewStage.Publish,
            CancellationToken.None);

        Assert.Equal(ComplianceKnowledgeReviewStatus.Failed, review.Status);
        Assert.Equal("compliance.foundry_invalid_output", review.ErrorCode);
    }

    [Fact]
    public async Task MissingJurisdictionRemainsAReviewableWarning()
    {
        var output = """
            {
              "status":"needs_review",
              "summary":"The postal code could not be mapped confidently.",
              "findings":[{
                "code":"MISSING_JURISDICTION",
                "message":"Confirm the applicable state before relying on state-specific guidance.",
                "fieldPath":"biller.postal_code",
                "jurisdiction":null,
                "sources":["https://www.census.gov/programs-surveys/geography/guidance/geo-areas/zctas.html"],
                "requiresReview":true
              }],
              "sources":[{
                "url":"https://www.census.gov/programs-surveys/geography/guidance/geo-areas/zctas.html",
                "title":"ZIP Code Tabulation Areas"
              }]
            }
            """;
        var reviewer = new FoundryComplianceKnowledgeReviewer(
            new RecordingFoundryGateway(new FoundryAgentOutput(output, [])),
            Options(true),
            NullLogger<FoundryComplianceKnowledgeReviewer>.Instance);

        var review = await reviewer.ReviewAsync(
            Biller(),
            Definition(),
            [],
            ComplianceReviewStage.Publish,
            CancellationToken.None);

        var finding = Assert.Single(review.Findings, item => item.Code == "MISSING_JURISDICTION");
        Assert.Equal(ComplianceFindingSeverity.Warning, finding.Severity);
        Assert.True(finding.RequiresReview);
        Assert.Null(finding.Jurisdiction);
    }

    [Fact]
    public async Task FoundryOutageBecomesBlockingWhenEvidenceIsRequired()
    {
        var reviewer = new FoundryComplianceKnowledgeReviewer(
            new FailingFoundryGateway(),
            Options(true),
            NullLogger<FoundryComplianceKnowledgeReviewer>.Instance);
        var service = new ComplianceReviewService(
            new CompliancePolicyEngine(Options(true)),
            Options(true),
            NullLogger<ComplianceReviewService>.Instance,
            reviewer);

        var findings = await service.ReviewAsync(
            Biller(),
            Definition(),
            ComplianceReviewStage.Publish,
            CancellationToken.None);

        Assert.Contains(findings, finding =>
            finding.Code == "COMPLIANCE_KNOWLEDGE_UNAVAILABLE" &&
            finding.Severity == ComplianceFindingSeverity.Blocking);
    }

    private static IOptions<BillerExperienceOptions> Options(bool requireFoundryEvidence) =>
        Microsoft.Extensions.Options.Options.Create(new BillerExperienceOptions
        {
            Compliance = new ComplianceOptions
            {
                FoundryAgentId = "biller-compliance",
                RequireFoundryEvidence = requireFoundryEvidence,
                PolicyVersion = "test-policy"
            }
        });

    private static BillerRecord Biller() =>
        new(
            "biller-1",
            "City of Vista",
            "city-of-vista",
            "Utility",
            "02110",
            new Uri("https://vista.example"),
            null,
            null,
            [new PaymentRailReference("card", "processor")],
            BillerStatus.Prospect,
            DateTimeOffset.UtcNow);

    private static BillerExperienceDefinition Definition() =>
        new(
            "1.1",
            "biller-1",
            new ExperienceBrand("City of Vista", "#174A5B", "#18B4E9", null, "Inter"),
            new ExperienceContent(
                "Pay your bill",
                "Welcome",
                "Support",
                new Uri("https://vista.example/privacy"),
                new Uri("https://vista.example/terms")),
            new PwaConfiguration("City of Vista", "Vista", "#174A5B", "#FFFFFF", null),
            ["card", "ach"],
            new ExperienceUi(
                "centered-card",
                new ExperienceTheme("comfortable", "rounded", "subtle"),
                [],
                [new ExperienceAction("primary-payment-action", "Pay Now", ExperienceActionType.StartPayment)]),
            new ExperiencePreferences(
                true,
                true,
                true,
                true,
                ReminderChannel.Both,
                ["card", "ach"],
                true,
                true,
                FeeHandling.Mixed,
                new PreviewPreferences("desktop", ["payment"])));

    private sealed class RecordingFoundryGateway(FoundryAgentOutput output) : IFoundryAgentServiceGateway
    {
        public string Prompt { get; private set; } = string.Empty;

        public Task<IReadOnlyList<FoundryAgentDefinition>> ListAgentsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FoundryAgentDefinition>>([]);

        public Task<FoundryAgentOutput> InvokeAsync(
            string agentId,
            string prompt,
            CancellationToken cancellationToken)
        {
            Prompt = prompt;
            return Task.FromResult(output);
        }
    }

    private sealed class FailingFoundryGateway : IFoundryAgentServiceGateway
    {
        public Task<IReadOnlyList<FoundryAgentDefinition>> ListAgentsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FoundryAgentDefinition>>([]);

        public Task<FoundryAgentOutput> InvokeAsync(
            string agentId,
            string prompt,
            CancellationToken cancellationToken) =>
            Task.FromException<FoundryAgentOutput>(
                new FoundryResearchException("agent_request_failed", "Foundry unavailable.", true));
    }
}
