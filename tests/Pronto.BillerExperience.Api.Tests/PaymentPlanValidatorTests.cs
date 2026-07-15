using Pronto.BillerExperience.Api.Application.Agents;
using Pronto.BillerExperience.Api.Domain;
using Pronto.Invoice.Contracts.V1.Invoices;
using Pronto.Payment.Contracts.V1.Payments;
using Xunit;

namespace Pronto.BillerExperience.Api.Tests;

/// <summary>
/// The gate between Financial Planning and Policy trusts the plan's choice but never its numbers:
/// method must be quoted, fee/total must match that quote exactly, and timing must not slip past the
/// due date.
/// </summary>
public sealed class PaymentPlanValidatorTests
{
    private static readonly PaymentPlanValidator Validator = new();

    private static readonly BillSummary Bill =
        new("i_77", 8420, new DateOnly(2026, 7, 25), "Water — July", InvoiceStatus.Due);

    private static readonly IReadOnlyList<PaymentQuoteResponse> Quotes =
    [
        new("b_1", "i_77", "card", 8420, 211, 8631),
        new("b_1", "i_77", "ach", 8420, 150, 8570),
    ];

    private static PaymentPlan Plan(string method, DateOnly? scheduledFor, int feeCents, int totalCents) =>
        new(method, scheduledFor, feeCents, totalCents, "because");

    [Fact]
    public void AcceptsAPlanThatMatchesItsQuote()
    {
        var result = Validator.Validate(Plan("ach", new DateOnly(2026, 7, 25), 150, 8570), Quotes, Bill);

        Assert.True(result.IsValid);
        Assert.Null(result.Code);
    }

    [Fact]
    public void RejectsAnUnquotedMethod()
    {
        var result = Validator.Validate(Plan("paypal", null, 150, 8570), Quotes, Bill);

        Assert.False(result.IsValid);
        Assert.Equal("method_not_quoted", result.Code);
    }

    [Fact]
    public void RejectsAFeeThatDoesNotMatchTheQuote()
    {
        var result = Validator.Validate(Plan("ach", null, 95, 8570), Quotes, Bill);

        Assert.False(result.IsValid);
        Assert.Equal("fee_mismatch", result.Code);
    }

    [Fact]
    public void RejectsATotalThatDoesNotMatchTheQuote()
    {
        var result = Validator.Validate(Plan("ach", null, 150, 9999), Quotes, Bill);

        Assert.False(result.IsValid);
        Assert.Equal("total_mismatch", result.Code);
    }

    [Fact]
    public void RejectsSchedulingPastTheDueDate()
    {
        var result = Validator.Validate(Plan("ach", new DateOnly(2026, 7, 26), 150, 8570), Quotes, Bill);

        Assert.False(result.IsValid);
        Assert.Equal("scheduled_past_due", result.Code);
    }

    [Fact]
    public void AllowsSchedulingExactlyOnTheDueDate()
    {
        var result = Validator.Validate(Plan("ach", new DateOnly(2026, 7, 25), 150, 8570), Quotes, Bill);

        Assert.True(result.IsValid);
    }
}
