using System.Security.Claims;
using Pronto.Invoice.Api.Common;
using Pronto.Invoice.Api.Controllers;
using Pronto.Invoice.Api.Repositories;
using Pronto.Invoice.Contracts.V1.Invoices;
using Pronto.ServiceDefaults.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Pronto.Invoice.Api.Tests;

public sealed class InvoicesControllerTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 14, 0, 0, 0, TimeSpan.Zero);

    private static InvoicesController NewController(out InMemoryInvoiceRepository repo)
    {
        repo = new InMemoryInvoiceRepository();
        // The HTTP pipeline authenticates before the action runs; these unit tests call the
        // action directly, so supply a full-access (cross-biller) principal to stand in for it.
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("roles", ServiceClaims.CrossBillerRole)], "Test"));
        return new InvoicesController(repo, new FixedTimeProvider(FixedNow))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = principal },
            },
        };
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

    [Fact]
    public async Task ListReturns200WithOpenInvoicesForAccount()
    {
        var controller = NewController(out _);
        await controller.Seed(
            "b_1", new SeedInvoicesRequest(Count: 3, AccountNumber: "ACCT-1"), CancellationToken.None);

        var result = await controller.List("b_1", "ACCT-1", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<InvoiceListResponse>(ok.Value);
        Assert.Equal(3, body.Invoices.Count);
        Assert.All(body.Invoices, i => Assert.Equal("ACCT-1", i.AccountNumber));
    }

    [Fact]
    public async Task ListReturnsEmptyListForUnknownAccount()
    {
        var controller = NewController(out _);
        await controller.Seed(
            "b_1", new SeedInvoicesRequest(Count: 2, AccountNumber: "ACCT-1"), CancellationToken.None);

        // Unknown account is a 200 with an empty list, not a 404 (see List() doc).
        var result = await controller.List("b_1", "ACCT-UNKNOWN", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<InvoiceListResponse>(ok.Value);
        Assert.Empty(body.Invoices);
    }

    [Fact]
    public async Task ListReturnsOnlyTheRequestedAccountsInvoices()
    {
        var controller = NewController(out _);
        await controller.Seed(
            "b_1", new SeedInvoicesRequest(Count: 2, AccountNumber: "ACCT-1"), CancellationToken.None);
        await controller.Seed(
            "b_1", new SeedInvoicesRequest(Count: 3, AccountNumber: "ACCT-2"), CancellationToken.None);

        var result = await controller.List("b_1", "ACCT-2", CancellationToken.None);

        var body = (InvoiceListResponse)Assert.IsType<OkObjectResult>(result).Value!;
        Assert.Equal(3, body.Invoices.Count);
        Assert.All(body.Invoices, i => Assert.Equal("ACCT-2", i.AccountNumber));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ListRejectsMissingAccountNumber(string? accountNumber)
    {
        var controller = NewController(out _);

        var result = await controller.List("b_1", accountNumber, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var error = Assert.IsType<ApiError>(badRequest.Value);
        Assert.Equal("invalid_account_number", error.Error.Code);
    }

    [Fact]
    public async Task ListRejectsBlankBillerId()
    {
        var controller = NewController(out _);

        // billerId is validated before account_number, mirroring Seed — a blank
        // biller must 400, not silently query with an invalid key and return [].
        var result = await controller.List("  ", "ACCT-1", CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var error = Assert.IsType<ApiError>(badRequest.Value);
        Assert.Equal("invalid_biller", error.Error.Code);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
