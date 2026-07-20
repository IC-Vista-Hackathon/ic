using Pronto.BillerExperience.Api.Application.Compliance;
using Pronto.BillerExperience.Api.Application.Compliance.Checkers;
using Pronto.BillerExperience.Api.Configuration;
using Pronto.BillerExperience.Api.Domain;
using Pronto.BillerExperience.Contracts.V1.Billers;
using Pronto.BillerExperience.Contracts.V1.Compliance;
using Pronto.BillerExperience.Contracts.V1.Experiences;
using Microsoft.Extensions.Options;
using Xunit;

namespace Pronto.BillerExperience.Api.Tests;

public sealed class ComplianceAttestationTests
{
    private const string SigningKey = "unit-test-compliance-attestation-signing-key";

    [Fact]
    public void AttestationOverCompliantConfigPassesAndVerifies()
    {
        var service = Service();
        var definition = CompliantDefinition();

        var attestation = service.Attest(Biller("02110"), definition, "config-3", 3);

        Assert.True(attestation.Passed);
        Assert.Empty(service.GatingFindings(attestation));
        Assert.Equal(5, attestation.Results.Count);
        Assert.All(attestation.Results, result => Assert.True(result.Passed));
        Assert.Equal("config-3", attestation.Revision);
        Assert.Equal(3, attestation.ConfigVersion);
        Assert.Equal(ComplianceAttestationSigner.Algorithm, attestation.SignatureAlgorithm);
        Assert.True(service.Verify(attestation, definition));
    }

    [Fact]
    public void AttestationOverNonCompliantConfigFailsButStillVerifies()
    {
        var service = Service();
        var definition = CompliantDefinition() with
        {
            Content = CompliantDefinition().Content with { RefundPolicyUrl = null }
        };

        var attestation = service.Attest(Biller("02110"), definition, "config-3", 3);

        Assert.False(attestation.Passed);
        Assert.Contains(service.GatingFindings(attestation), finding => finding.Code == "LEGAL_REFUND_POLICY_REQUIRED");
        // The attestation of a failing revision is itself a valid, signed record.
        Assert.True(service.Verify(attestation, definition));
    }

    [Fact]
    public void VerificationFailsWhenConfigIsTampered()
    {
        var service = Service();
        var definition = CompliantDefinition();
        var attestation = service.Attest(Biller("02110"), definition, "config-3", 3);

        var tampered = definition with
        {
            Brand = definition.Brand with { PrimaryColor = "#FFFFFF" }
        };

        Assert.False(service.Verify(attestation, tampered));
    }

    [Fact]
    public void VerificationFailsWhenResultsAreTampered()
    {
        var service = Service();
        var definition = CompliantDefinition() with
        {
            Content = CompliantDefinition().Content with { RefundPolicyUrl = null }
        };
        var attestation = service.Attest(Biller("02110"), definition, "config-3", 3);

        // Flip the failing result to passing while leaving the signed hashes untouched.
        var forgedResults = attestation.Results
            .Select(result => result.CheckerId == LegalLinksChecker.Id
                ? result with { Passed = true, Findings = [] }
                : result)
            .ToArray();
        var forged = attestation with { Results = forgedResults, Passed = true };

        Assert.False(service.Verify(forged, definition));
    }

    [Fact]
    public void VerificationFailsWhenSignatureIsTampered()
    {
        var service = Service();
        var definition = CompliantDefinition();
        var attestation = service.Attest(Biller("02110"), definition, "config-3", 3);

        var forged = attestation with { Signature = Convert.ToBase64String(new byte[32]) };

        Assert.False(service.Verify(forged, definition));
    }

    [Fact]
    public void VerificationFailsWhenRecordedHashIsTampered()
    {
        var service = Service();
        var definition = CompliantDefinition();
        var attestation = service.Attest(Biller("02110"), definition, "config-3", 3);

        var forged = attestation with { ConfigHash = new string('0', attestation.ConfigHash.Length) };

        Assert.False(service.Verify(forged, definition));
    }

    [Fact]
    public void VerificationFailsUnderADifferentSigningKey()
    {
        var definition = CompliantDefinition();
        var attestation = Service().Attest(Biller("02110"), definition, "config-3", 3);

        var otherSigner = new ComplianceAttestationSigner("a-totally-different-signing-key-value");

        Assert.False(otherSigner.Verify(attestation, definition));
    }

    [Fact]
    public void SigningRequiresANonEmptyKey()
    {
        Assert.Throws<ArgumentException>(() => new ComplianceAttestationSigner(" "));
    }

    private static ComplianceAttestationService Service() =>
        new(
            ComplianceCheckerCatalog.CreateDefault(),
            new ComplianceAttestationSigner(SigningKey),
            Options.Create(new BillerExperienceOptions
            {
                Compliance = new ComplianceOptions { PolicyVersion = "test-policy" }
            }));

    private static BillerRecord Biller(string postalCode) =>
        new(
            "biller-1",
            "City of Vista",
            "city-of-vista",
            "Utility",
            postalCode,
            new Uri("https://vista.example"),
            null,
            null,
            [new PaymentRailReference("card", "processor")],
            BillerStatus.Prospect,
            DateTimeOffset.UtcNow);

    private static BillerExperienceDefinition CompliantDefinition() =>
        new(
            "1.1",
            "biller-1",
            new ExperienceBrand("City of Vista", "#085368", "#18B4E9", null, "Inter"),
            new ExperienceContent(
                "Pay your bill",
                "Welcome",
                "Support",
                new Uri("https://vista.example/privacy"),
                new Uri("https://vista.example/terms"),
                new Uri("https://vista.example/refunds"),
                "A service fee applies and is shown before you confirm."),
            new PwaConfiguration("City of Vista", "Vista", "#085368", "#FFFFFF", null),
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
}
