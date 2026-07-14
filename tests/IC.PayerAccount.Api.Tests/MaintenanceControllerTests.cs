using IC.PayerAccount.Api;
using IC.PayerAccount.Api.Controllers;
using IC.PayerAccount.Api.Storage;
using IC.ServiceDefaults.Errors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Xunit;

namespace IC.PayerAccount.Api.Tests;

public sealed class MaintenanceControllerTests
{
    private static MaintenanceController NewController(bool purgeEnabled) =>
        new(new InMemoryPayerStore(), Options.Create(new MaintenanceOptions { PurgeEnabled = purgeEnabled }));

    [Fact]
    public async Task PurgeReturns404WhenDisabled()
    {
        var result = await NewController(purgeEnabled: false).Purge("b_1", CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task PurgeThrowsBadRequestForBlankBillerWhenEnabled()
    {
        var controller = NewController(purgeEnabled: true);

        var exception = await Assert.ThrowsAsync<ServiceException>(
            () => controller.Purge("  ", CancellationToken.None));

        Assert.Equal("invalid_biller", exception.Code);
        Assert.Equal(StatusCodes.Status400BadRequest, exception.StatusCode);
    }

    [Fact]
    public async Task PurgeReturns204WhenEnabled()
    {
        var result = await NewController(purgeEnabled: true).Purge("b_1", CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }
}
