using System.Security.Claims;
using Pronto.Invoice.Api.Common;
using Pronto.Invoice.Api.Controllers;
using Pronto.Invoice.Api.Domain;
using Pronto.Invoice.Api.Repositories;
using Pronto.Invoice.Contracts.V1.Invoices;
using Pronto.ServiceDefaults.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;
using WireStatus = Pronto.Invoice.Contracts.V1.Invoices.InvoiceStatus;

namespace Pronto.Invoice.Api.Tests;

/// <summary>
/// Covers the conditional status transition added for the Payment Service:
/// repository atomicity/idempotency rules and the controller endpoints over them.
/// </summary>
public sealed class InvoiceStatusTransitionTests
{
    private static InvoiceDocument Make(
        string billerId,
        Domain.InvoiceStatus status = Domain.InvoiceStatus.Due,
        string? paymentId = null) => new()
    {
        Id = Guid.NewGuid().ToString(),
        BillerId = billerId,
        AccountNumber = "ACCT-1",
        PayerName = "Test Payer",
        Description = "Test",
        AmountCents = 1000,
        DueDate = new DateOnly(2026, 8, 1),
        Status = status,
        LastPaymentId = paymentId,
    };

    private static InvoicesController NewController(out InMemoryInvoiceRepository repo)
    {
        repo = new InMemoryInvoiceRepository();
        // Stand in for the authentication the HTTP pipeline would perform before the action.
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("roles", ServiceClaims.CrossBillerRole)], "Test"));
        return new InvoicesController(repo, TimeProvider.System)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = principal },
            },
        };
    }

    [Theory]
    [InlineData(Domain.InvoiceStatus.Due, Domain.InvoiceStatus.Paid)]
    [InlineData(Domain.InvoiceStatus.Due, Domain.InvoiceStatus.Scheduled)]
    [InlineData(Domain.InvoiceStatus.Scheduled, Domain.InvoiceStatus.Paid)]
    public async Task AllowedTransitionsUpdateTheInvoice(
        Domain.InvoiceStatus from, Domain.InvoiceStatus to)
    {
        var repo = new InMemoryInvoiceRepository();
        var invoice = Make("b_1", from);
        await repo.AddRangeAsync([invoice]);

        var result = await repo.TryUpdateStatusAsync("b_1", invoice.Id, to, "pay-1");

        Assert.Equal(InvoiceTransitionOutcome.Updated, result.Outcome);
        Assert.Equal(to, result.Invoice!.Status);
        Assert.Equal("pay-1", result.Invoice.LastPaymentId);
    }

    [Fact]
    public async Task PaidInvoiceRejectsAnotherPayment()
    {
        var repo = new InMemoryInvoiceRepository();
        var invoice = Make("b_1");
        await repo.AddRangeAsync([invoice]);
        await repo.TryUpdateStatusAsync("b_1", invoice.Id, Domain.InvoiceStatus.Paid, "pay-1");

        var second = await repo.TryUpdateStatusAsync("b_1", invoice.Id, Domain.InvoiceStatus.Paid, "pay-2");

        Assert.Equal(InvoiceTransitionOutcome.AlreadyPaid, second.Outcome);
    }

    [Fact]
    public async Task SamePaymentReplayIsIdempotent()
    {
        var repo = new InMemoryInvoiceRepository();
        var invoice = Make("b_1");
        await repo.AddRangeAsync([invoice]);
        await repo.TryUpdateStatusAsync("b_1", invoice.Id, Domain.InvoiceStatus.Paid, "pay-1");

        var replay = await repo.TryUpdateStatusAsync("b_1", invoice.Id, Domain.InvoiceStatus.Paid, "pay-1");

        Assert.Equal(InvoiceTransitionOutcome.Updated, replay.Outcome);
        Assert.Equal(Domain.InvoiceStatus.Paid, replay.Invoice!.Status);
    }

    [Fact]
    public async Task PaidToScheduledIsInvalid()
    {
        var repo = new InMemoryInvoiceRepository();
        var invoice = Make("b_1", Domain.InvoiceStatus.Paid, "pay-1");
        await repo.AddRangeAsync([invoice]);

        var result = await repo.TryUpdateStatusAsync(
            "b_1", invoice.Id, Domain.InvoiceStatus.Scheduled, "pay-2");

        Assert.Equal(InvoiceTransitionOutcome.AlreadyPaid, result.Outcome);
    }

    [Fact]
    public async Task UnknownInvoiceOrBillerIsNotFound()
    {
        var repo = new InMemoryInvoiceRepository();
        var invoice = Make("b_1");
        await repo.AddRangeAsync([invoice]);

        var wrongBiller = await repo.TryUpdateStatusAsync(
            "b_2", invoice.Id, Domain.InvoiceStatus.Paid, "pay-1");
        var wrongInvoice = await repo.TryUpdateStatusAsync(
            "b_1", Guid.NewGuid().ToString(), Domain.InvoiceStatus.Paid, "pay-1");

        Assert.Equal(InvoiceTransitionOutcome.NotFound, wrongBiller.Outcome);
        Assert.Equal(InvoiceTransitionOutcome.NotFound, wrongInvoice.Outcome);
    }

    [Fact]
    public async Task FindIsPartitionScoped()
    {
        var repo = new InMemoryInvoiceRepository();
        var invoice = Make("b_1");
        await repo.AddRangeAsync([invoice]);

        Assert.NotNull(await repo.FindAsync("b_1", invoice.Id));
        Assert.Null(await repo.FindAsync("b_2", invoice.Id));
    }

    [Fact]
    public async Task ControllerRejectsDueAsTarget()
    {
        var controller = NewController(out var repo);
        var invoice = Make("b_1");
        await repo.AddRangeAsync([invoice]);

        var result = await controller.UpdateStatus(
            "b_1", invoice.Id, new UpdateInvoiceStatusRequest(WireStatus.Due, "pay-1"), CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("invalid_status", ((ApiError)badRequest.Value!).Error.Code);
    }

    [Fact]
    public async Task ControllerMapsAlreadyPaidToConflict()
    {
        var controller = NewController(out var repo);
        var invoice = Make("b_1", Domain.InvoiceStatus.Paid, "pay-1");
        await repo.AddRangeAsync([invoice]);

        var result = await controller.UpdateStatus(
            "b_1", invoice.Id, new UpdateInvoiceStatusRequest(WireStatus.Paid, "pay-2"), CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal("already_paid", ((ApiError)conflict.Value!).Error.Code);
    }

    [Fact]
    public async Task ControllerGetReturnsInvoiceAndNotFound()
    {
        var controller = NewController(out var repo);
        var invoice = Make("b_1");
        await repo.AddRangeAsync([invoice]);

        var found = await controller.Get("b_1", invoice.Id, CancellationToken.None);
        var missing = await controller.Get("b_1", Guid.NewGuid().ToString(), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(found);
        Assert.Equal(invoice.Id, ((InvoiceResponse)ok.Value!).Id);
        Assert.IsType<NotFoundObjectResult>(missing);
    }

    [Fact]
    public async Task ControllerListReturnsOpenInvoicesAndRequiresAccountNumber()
    {
        var controller = NewController(out var repo);
        await repo.AddRangeAsync([Make("b_1"), Make("b_1", Domain.InvoiceStatus.Paid, "pay-0")]);

        var listed = await controller.List("b_1", "ACCT-1", CancellationToken.None);
        var missingAccount = await controller.List("b_1", null, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(listed);
        Assert.Single(((InvoiceListResponse)ok.Value!).Invoices);
        Assert.IsType<BadRequestObjectResult>(missingAccount);
    }

    [Fact]
    public async Task ControllerListCanIncludePaidInvoiceHistory()
    {
        var controller = NewController(out var repo);
        await repo.AddRangeAsync([Make("b_1"), Make("b_1", Domain.InvoiceStatus.Paid, "pay-0")]);

        var listed = await controller.List("b_1", "ACCT-1", CancellationToken.None, includeClosed: true);

        var body = Assert.IsType<InvoiceListResponse>(Assert.IsType<OkObjectResult>(listed).Value);
        Assert.Equal(2, body.Invoices.Count);
        Assert.Contains(body.Invoices, invoice => invoice.Status == WireStatus.Paid);
    }

    [Fact]
    public async Task ControllerListRejectsBlankBillerId()
    {
        var controller = NewController(out _);

        // billerId is validated before account_number — a blank biller must 400
        // with invalid_biller even when a valid account_number is supplied.
        var result = await controller.List("  ", "ACCT-1", CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("invalid_biller", ((ApiError)badRequest.Value!).Error.Code);
    }

    [Fact]
    public async Task ControllerGetRejectsBlankBillerId()
    {
        var controller = NewController(out _);

        var result = await controller.Get("  ", "i_1", CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("invalid_biller", ((ApiError)badRequest.Value!).Error.Code);
    }

    [Fact]
    public async Task ControllerUpdateStatusRejectsBlankBillerId()
    {
        var controller = NewController(out _);

        // billerId is validated before status/payment_id — a blank biller must 400
        // with invalid_biller, not fall through to a status/payment check.
        var result = await controller.UpdateStatus(
            "  ", "i_1", new UpdateInvoiceStatusRequest(WireStatus.Paid, "pay-1"), CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("invalid_biller", ((ApiError)badRequest.Value!).Error.Code);
    }
}
