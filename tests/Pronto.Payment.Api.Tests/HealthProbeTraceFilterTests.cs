using Microsoft.AspNetCore.Http;
using Pronto.ServiceDefaults;
using Xunit;

namespace Pronto.Payment.Api.Tests;

public sealed class HealthProbeTraceFilterTests
{
    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    [InlineData("/HEALTH/LIVE")]
    public void HealthProbeRequestsAreIdentified(string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;

        Assert.True(ServiceDefaultsExtensions.IsHealthProbeRequest(context));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/payments")]
    [InlineData("/health")]
    [InlineData("/health/livez")]
    public void NormalRequestsAreNotFiltered(string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;

        Assert.False(ServiceDefaultsExtensions.IsHealthProbeRequest(context));
    }
}
