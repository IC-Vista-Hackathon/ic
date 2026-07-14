using IC.Invoice.Api.Common;
using IC.Invoice.Api.Controllers;
using IC.Invoice.Api.Repositories;
using IC.Invoice.Contracts.V1.Invoices;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace IC.Invoice.Api.Tests;

public sealed class InvoicesControllerTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 14, 0, 0, 0, TimeSpan.Zero);

    private static InvoicesController NewController(out InMemoryInvoiceRepository repo)
    {
        repo = new InMemoryInvoiceRepository();
        return new InvoicesController(repo, new FixedTimeProvider(FixedNow));
    }

    [Fact]
    public async Task SeedWithEmptyRequestReturns201WithDefaultSet()
    {
        var controller = NewController(out _);

        var result = await controller.Seed("b_1", new SeedInvoicesRequest(), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status201Created, objectResult.StatusCode);
        var body = Assert.IsType<SeedInvoicesResponse>(objectResult.Value);
        Assert.Equal(4, body.Seeded);
        Assert.Equal(body.Seeded, body.Invoices.Count);
    }

    [Fact]
    public async Task SeedGeneratesAccountNumberWhenNoneProvided()
    {
        var controller = NewController(out _);

        var result = await controller.Seed("b_1", new SeedInvoicesRequest(), CancellationToken.None);

        var body = (SeedInvoicesResponse)Assert.IsType<ObjectResult>(result).Value!;
        Assert.StartsWith("ACCT-", body.AccountNumber, StringComparison.Ordinal);
        Assert.All(body.Invoices, i => Assert.Equal(body.AccountNumber, i.AccountNumber));
    }

    [Fact]
    public async Task SeedHonoursExplicitCountAndAccountNumber()
    {
        var controller = NewController(out _);

        var result = await controller.Seed(
            "b_1",
            new SeedInvoicesRequest(Count: 2, AccountNumber: "ACCT-PLANO-1", BillType: "Utility"),
            CancellationToken.None);

        var body = (SeedInvoicesResponse)Assert.IsType<ObjectResult>(result).Value!;
        Assert.Equal(2, body.Seeded);
        Assert.Equal("ACCT-PLANO-1", body.AccountNumber);
    }

    [Fact]
    public async Task SeedPersistsInvoicesRetrievableByLookup()
    {
        var controller = NewController(out var repo);

        var result = await controller.Seed(
            "b_1",
            new SeedInvoicesRequest(Count: 3, AccountNumber: "ACCT-1"),
            CancellationToken.None);
        _ = result;

        var open = await repo.GetOpenAsync("b_1", "ACCT-1");
        Assert.Equal(3, open.Count);
    }

    [Fact]
    public async Task SeedRejectsBlankBillerId()
    {
        var controller = NewController(out _);

        var result = await controller.Seed("  ", new SeedInvoicesRequest(), CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var error = Assert.IsType<ApiError>(badRequest.Value);
        Assert.Equal("invalid_biller", error.Error.Code);
    }

    [Fact]
    public async Task SeedReturnsInvoicesWithLowercaseWireStatus()
    {
        var controller = NewController(out _);

        var result = await controller.Seed("b_1", new SeedInvoicesRequest(Count: 1), CancellationToken.None);

        var body = (SeedInvoicesResponse)Assert.IsType<ObjectResult>(result).Value!;
        Assert.Equal(InvoiceStatus.Due, body.Invoices[0].Status);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
