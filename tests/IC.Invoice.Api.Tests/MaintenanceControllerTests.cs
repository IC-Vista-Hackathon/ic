using IC.Invoice.Api;
using IC.Invoice.Api.Common;
using IC.Invoice.Api.Controllers;
using IC.Invoice.Api.Repositories;
using IC.Invoice.Contracts.V1.Invoices;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Xunit;

namespace IC.Invoice.Api.Tests;

public sealed class MaintenanceControllerTests
{
    private static MaintenanceController NewController(bool purgeEnabled, out InMemoryInvoiceRepository repo)
    {
        repo = new InMemoryInvoiceRepository();
        return new MaintenanceController(repo, Options.Create(new MaintenanceOptions { PurgeEnabled = purgeEnabled }));
    }

    [Fact]
    public async Task PurgeReturns404WhenDisabled()
    {
        var controller = NewController(purgeEnabled: false, out _);

        var result = await controller.Purge("b_1", CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task PurgeRejectsBlankBillerIdWhenEnabled()
    {
        var controller = NewController(purgeEnabled: true, out _);

        var result = await controller.Purge("  ", CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("invalid_biller", ((ApiError)badRequest.Value!).Error.Code);
    }

    [Fact]
    public async Task PurgeDeletesBillerDataWhenEnabled()
    {
        var controller = NewController(purgeEnabled: true, out var repo);
        await repo.AddRangeAsync(
        [
            new Domain.InvoiceDocument
            {
                Id = Guid.NewGuid().ToString(),
                BillerId = "b_1",
                AccountNumber = "ACCT-1",
                PayerName = "P",
                Description = "D",
                AmountCents = 100,
                DueDate = new DateOnly(2026, 8, 1),
                Status = Domain.InvoiceStatus.Due,
            },
        ]);

        var result = await controller.Purge("b_1", CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Empty(await repo.GetOpenAsync("b_1", "ACCT-1"));
    }
}
