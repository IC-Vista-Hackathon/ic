using Pronto.BillerExperience.Api.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Pronto.BillerExperience.Api.Tests;

public sealed class TelemetryControllerTests
{
    [Fact]
    public void ReturnsConnectionStringFromConfiguration()
    {
        var controller = Controller(new Dictionary<string, string?>
        {
            [TelemetryController.ConnectionStringKey] = "InstrumentationKey=00000000-0000-0000-0000-000000000000",
        });

        var response = Assert.IsType<BrowserTelemetryConfigurationResponse>(
            Assert.IsType<OkObjectResult>(controller.Get().Result).Value);

        Assert.Equal("InstrumentationKey=00000000-0000-0000-0000-000000000000", response.ConnectionString);
        Assert.Equal(100d, response.SamplingPercentage);
    }

    [Fact]
    public void ReturnsNullConnectionStringWhenUnconfigured()
    {
        var controller = Controller([]);

        var response = Assert.IsType<BrowserTelemetryConfigurationResponse>(
            Assert.IsType<OkObjectResult>(controller.Get().Result).Value);

        Assert.Null(response.ConnectionString);
    }

    [Fact]
    public void TreatsWhitespaceConnectionStringAsUnconfigured()
    {
        var controller = Controller(new Dictionary<string, string?>
        {
            [TelemetryController.ConnectionStringKey] = "   ",
        });

        var response = Assert.IsType<BrowserTelemetryConfigurationResponse>(
            Assert.IsType<OkObjectResult>(controller.Get().Result).Value);

        Assert.Null(response.ConnectionString);
    }

    [Fact]
    public void ClampsSamplingPercentageIntoValidRange()
    {
        var controller = Controller(new Dictionary<string, string?>
        {
            [TelemetryController.SamplingPercentageKey] = "250",
        });

        var response = Assert.IsType<BrowserTelemetryConfigurationResponse>(
            Assert.IsType<OkObjectResult>(controller.Get().Result).Value);

        Assert.Equal(100d, response.SamplingPercentage);
    }

    [Fact]
    public void MarksResponseCacheable()
    {
        var controller = Controller([]);

        controller.Get();

        Assert.Equal("public, max-age=300", controller.Response.Headers.CacheControl);
    }

    private static TelemetryController Controller(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return new TelemetryController(configuration)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
    }
}
