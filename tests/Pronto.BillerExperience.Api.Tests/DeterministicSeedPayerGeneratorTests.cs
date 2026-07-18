using System.Net.Mail;
using Pronto.BillerExperience.Api.Infrastructure.SupportingServices;
using Xunit;

namespace Pronto.BillerExperience.Api.Tests;

/// <summary>
/// The demo payer generator is the "agent configures" half of payer seeding: it must be
/// deterministic (so re-seeding a biller is idempotent) and distinct across billers (so two
/// seeded billers don't collide on one demo payer). Mirrors the invoice generator's guarantees.
/// </summary>
public sealed class DeterministicSeedPayerGeneratorTests
{
    private readonly DeterministicSeedPayerGenerator _generator = new();

    private static SeedBillerContext Biller(string id = "b-1", string name = "Riverside Water") =>
        new(id, name, "utility", new Uri("https://riverside.example"));

    [Fact]
    public void SameBillerYieldsIdenticalPayer()
    {
        var first = _generator.Generate(Biller());
        var second = _generator.Generate(Biller());

        Assert.Equal(first.Name, second.Name);
        Assert.Equal(first.Email, second.Email);
        Assert.Equal(first.Autopay, second.Autopay);
        Assert.Equal(first.Paperless, second.Paperless);
        Assert.Equal(first.PaymentDay, second.PaymentDay);
        Assert.Equal(first.Channels, second.Channels);
    }

    [Fact]
    public void DifferentBillersYieldDifferentPayers()
    {
        var a = _generator.Generate(Biller("b-a", "Acme Water"));
        var b = _generator.Generate(Biller("b-b", "Zenith Power"));

        Assert.NotEqual(a.Email, b.Email);
    }

    [Fact]
    public void PayerEmailIsAValidAddress()
    {
        var spec = _generator.Generate(Biller());

        Assert.True(MailAddress.TryCreate(spec.Email, out var parsed));
        Assert.Equal(spec.Email, parsed!.Address);
    }

    [Fact]
    public void DefaultsAreConservativeAndEmailOptedIn()
    {
        var spec = _generator.Generate(Biller());

        Assert.False(spec.Autopay);
        Assert.False(spec.Paperless);
        Assert.Null(spec.PaymentDay);
        Assert.Contains("email", spec.Channels);
        Assert.False(string.IsNullOrWhiteSpace(spec.Name));
    }
}
