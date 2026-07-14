using Pronto.BillerExperience.Api;
using Pronto.BillerExperience.Api.Controllers;
using Pronto.BillerExperience.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Xunit;

namespace Pronto.BillerExperience.Api.Tests;

public sealed class MaintenanceControllerTests
{
    private static MaintenanceController NewController(bool purgeEnabled) =>
        new(
            new InMemoryBillerExperienceRepository(),
            Options.Create(new MaintenanceOptions { PurgeEnabled = purgeEnabled }));

    [Fact]
    public async Task PurgeReturns404WhenDisabled()
    {
        var result = await NewController(purgeEnabled: false).Purge("b_1", CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task PurgeRejectsBlankBillerIdWhenEnabled()
    {
        var result = await NewController(purgeEnabled: true).Purge("  ", CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task PurgeReturns204WhenEnabled()
    {
        var result = await NewController(purgeEnabled: true).Purge("b_1", CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }
}
