using Pronto.BillerExperience.Api.Application.Compliance.Checkers;
using Pronto.BillerExperience.Api.Domain;
using Pronto.BillerExperience.Contracts.V1.Billers;
using Pronto.BillerExperience.Contracts.V1.Experiences;
using Xunit;

namespace Pronto.BillerExperience.Api.Tests;

public sealed class ComplianceCheckerTests
{
    private const string PolicyVersion = "test-policy";

    // --- Fee disclosure ---

    [Fact]
    public void FeeDisclosurePassesWhenFeesAbsorbed()
    {
        var context = Context(Definition() with
        {
            Content = Content() with { FeeDisclosure = null },
            Preferences = Preferences() with { FeeHandling = FeeHandling.Absorb }
        });

        var result = new FeeDisclosureChecker().Check(context);

        Assert.True(result.Passed);
        Assert.Empty(result.Findings);
        Assert.Equal(FeeDisclosureChecker.Id, result.CheckerId);
        Assert.Equal(PolicyVersion, result.PolicyVersion);
    }

    [Theory]
    [InlineData(FeeHandling.Charge)]
    [InlineData(FeeHandling.Mixed)]
    public void FeeDisclosurePassesWhenFeesChargedAndDisclosed(FeeHandling handling)
    {
        var context = Context(Definition() with
        {
            Content = Content() with { FeeDisclosure = "A service fee applies and is shown before you confirm." },
            Preferences = Preferences() with { FeeHandling = handling }
        });

        Assert.True(new FeeDisclosureChecker().Check(context).Passed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("too short")]
    public void FeeDisclosureFailsWhenFeesChargedWithoutDisclosure(string? disclosure)
    {
        var context = Context(Definition() with
        {
            Content = Content() with { FeeDisclosure = disclosure },
            Preferences = Preferences() with { FeeHandling = FeeHandling.Charge }
        });

        var result = new FeeDisclosureChecker().Check(context);

        Assert.False(result.Passed);
        var finding = Assert.Single(result.Findings);
        Assert.Equal("FEE_DISCLOSURE_REQUIRED", finding.Code);
        Assert.Equal("content.fee_disclosure", finding.FieldPath);
        Assert.Equal(ComplianceFindingSeverity.Blocking, finding.Severity);
        Assert.False(finding.RequiresReview);
    }

    // --- Payment method jurisdiction ---

    [Fact]
    public void PaymentJurisdictionPassesForPermittedMethods()
    {
        var context = Context(Definition(), Biller("02110")); // MA
        var result = new PaymentMethodJurisdictionChecker(JurisdictionPaymentMethodPolicy.Default).Check(context);

        Assert.True(result.Passed);
    }

    [Fact]
    public void PaymentJurisdictionFailsWhenPostalCodeUnresolvable()
    {
        var context = Context(Definition(), Biller(""));
        var result = new PaymentMethodJurisdictionChecker(JurisdictionPaymentMethodPolicy.Default).Check(context);

        Assert.False(result.Passed);
        var finding = Assert.Single(result.Findings);
        Assert.Equal("PAYMENT_METHOD_JURISDICTION_UNKNOWN", finding.Code);
    }

    [Fact]
    public void PaymentJurisdictionFailsWhenMethodNotAvailableInJurisdiction()
    {
        // Puerto Rico (ZIP 006xx) permits card/wallet rails only — ACH is unavailable.
        var context = Context(
            Definition() with
            {
                EnabledPaymentCapabilities = ["card", "ach"],
                Preferences = Preferences() with { AcceptedMethods = ["card", "ach"] }
            },
            Biller("00601"));

        var result = new PaymentMethodJurisdictionChecker(JurisdictionPaymentMethodPolicy.Default).Check(context);

        Assert.False(result.Passed);
        var finding = Assert.Single(result.Findings);
        Assert.Equal("PAYMENT_METHOD_NOT_PERMITTED_IN_JURISDICTION", finding.Code);
        Assert.Equal("PR", finding.Jurisdiction);
        Assert.Contains("ach", finding.Message, StringComparison.Ordinal);
    }

    // --- Legal links ---

    [Fact]
    public void LegalLinksPassWhenAllPresentAndHttps()
    {
        Assert.True(new LegalLinksChecker().Check(Context(Definition())).Passed);
    }

    [Fact]
    public void LegalLinksFailWhenRefundPolicyMissing()
    {
        var context = Context(Definition() with { Content = Content() with { RefundPolicyUrl = null } });
        var result = new LegalLinksChecker().Check(context);

        Assert.False(result.Passed);
        var finding = Assert.Single(result.Findings);
        Assert.Equal("LEGAL_REFUND_POLICY_REQUIRED", finding.Code);
        Assert.Equal("content.refund_policy_url", finding.FieldPath);
    }

    [Fact]
    public void LegalLinksFailWhenTermsIsNotHttps()
    {
        var context = Context(Definition() with
        {
            Content = Content() with { TermsOfServiceUrl = new Uri("http://vista.example/terms") }
        });

        var result = new LegalLinksChecker().Check(context);

        Assert.False(result.Passed);
        Assert.Contains(result.Findings, finding => finding.Code == "LEGAL_TERMS_LINK_REQUIRED");
    }

    [Fact]
    public void LegalLinksReportEveryMissingLink()
    {
        var context = Context(Definition() with
        {
            Content = new ExperienceContent(
                "Pay",
                "Welcome",
                "Support",
                new Uri("http://vista.example/privacy"),
                new Uri("http://vista.example/terms"),
                RefundPolicyUrl: null,
                FeeDisclosure: null)
        });

        var result = new LegalLinksChecker().Check(context);

        Assert.False(result.Passed);
        Assert.Equal(3, result.Findings.Count);
    }

    // --- WCAG color contrast ---

    [Fact]
    public void ColorContrastPassesForHighContrastPalette()
    {
        var context = Context(Definition() with
        {
            Brand = Brand() with { PrimaryColor = "#085368" },
            Pwa = Pwa() with { BackgroundColor = "#FFFFFF" }
        });

        Assert.True(new ColorContrastChecker().Check(context).Passed);
    }

    [Fact]
    public void ColorContrastFailsForLowContrastPalette()
    {
        var context = Context(Definition() with
        {
            Brand = Brand() with { PrimaryColor = "#EEEEEE" },
            Pwa = Pwa() with { BackgroundColor = "#FFFFFF" }
        });

        var result = new ColorContrastChecker().Check(context);

        Assert.False(result.Passed);
        var finding = Assert.Single(result.Findings);
        Assert.Equal("BRAND_CONTRAST_INSUFFICIENT", finding.Code);
        Assert.Equal("brand.primary_color", finding.FieldPath);
    }

    [Fact]
    public void ColorContrastFailsWhenColorUnparseable()
    {
        var context = Context(Definition() with
        {
            Brand = Brand() with { PrimaryColor = "not-a-color" }
        });

        var result = new ColorContrastChecker().Check(context);

        Assert.False(result.Passed);
        Assert.Equal("BRAND_CONTRAST_UNVERIFIABLE", Assert.Single(result.Findings).Code);
    }

    [Fact]
    public void WcagContrastRatioMatchesKnownReference()
    {
        Assert.True(WcagContrast.TryParseHex("#000000", out var black));
        Assert.True(WcagContrast.TryParseHex("#FFFFFF", out var white));

        Assert.Equal(21.0, WcagContrast.Ratio(black, white), 3);
        Assert.Equal(1.0, WcagContrast.Ratio(white, white), 3);
    }

    // --- Telemetry PII posture ---

    [Fact]
    public void TelemetryPassesWhenNoPolicyConfigured()
    {
        Assert.True(new TelemetryPiiChecker().Check(Context(Definition() with { Telemetry = null })).Passed);
    }

    [Fact]
    public void TelemetryPassesForNonPiiFields()
    {
        var context = Context(Definition() with
        {
            Telemetry = new TelemetryPolicy(true, ["flow_id", "step", "amount_bucket"])
        });

        Assert.True(new TelemetryPiiChecker().Check(context).Passed);
    }

    [Theory]
    [InlineData("payer_email")]
    [InlineData("customerName")]
    [InlineData("card_number")]
    [InlineData("ssn")]
    [InlineData("street_address")]
    public void TelemetryFailsWhenPiiFieldConfigured(string field)
    {
        var context = Context(Definition() with
        {
            Telemetry = new TelemetryPolicy(true, ["flow_id", field])
        });

        var result = new TelemetryPiiChecker().Check(context);

        Assert.False(result.Passed);
        var finding = Assert.Single(result.Findings);
        Assert.Equal("TELEMETRY_PII_CONFIGURED", finding.Code);
        Assert.Equal("telemetry.captured_fields", finding.FieldPath);
    }

    // --- Postal jurisdiction resolver ---

    [Theory]
    [InlineData("02110", "MA")]
    [InlineData("10001", "NY")]
    [InlineData("90001", "CA")]
    [InlineData("00601", "PR")]
    [InlineData("99501", "AK")]
    public void ResolverMapsPostalCodesToStates(string postal, string expected)
    {
        Assert.Equal(expected, UsPostalJurisdictionResolver.ResolveStateCode(postal));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ab")]
    public void ResolverReturnsNullForUnresolvable(string? postal)
    {
        Assert.Null(UsPostalJurisdictionResolver.ResolveStateCode(postal));
    }

    private static ComplianceCheckContext Context(BillerExperienceDefinition definition, BillerRecord? biller = null) =>
        new(biller ?? Biller("02110"), definition, PolicyVersion);

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

    private static ExperienceBrand Brand() => new("City of Vista", "#085368", "#18B4E9", null, "Inter");

    private static PwaConfiguration Pwa() => new("City of Vista", "Vista", "#085368", "#FFFFFF", null);

    private static ExperienceContent Content() =>
        new(
            "Pay your bill",
            "Welcome",
            "Support",
            new Uri("https://vista.example/privacy"),
            new Uri("https://vista.example/terms"),
            new Uri("https://vista.example/refunds"),
            "A service fee applies and is shown before you confirm.");

    private static ExperiencePreferences Preferences() =>
        new(
            true,
            true,
            true,
            true,
            ReminderChannel.Both,
            ["card", "ach"],
            true,
            true,
            FeeHandling.Mixed,
            new PreviewPreferences("desktop", ["payment"]));

    private static BillerExperienceDefinition Definition() =>
        new(
            "1.1",
            "biller-1",
            Brand(),
            Content(),
            Pwa(),
            ["card", "ach"],
            new ExperienceUi(
                "centered-card",
                new ExperienceTheme("comfortable", "rounded", "subtle"),
                [],
                [new ExperienceAction("primary-payment-action", "Pay Now", ExperienceActionType.StartPayment)]),
            Preferences());
}
