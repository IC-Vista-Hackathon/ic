using Pronto.ServiceDefaults;
using Xunit;

namespace Pronto.ServiceDefaults.Tests;

public class PreviewTenantTests
{
    [Theory]
    [InlineData("abc123", false)]
    [InlineData("preview-abc123", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsPreviewDetectsMarker(string? billerId, bool expected)
    {
        Assert.Equal(expected, PreviewTenant.IsPreview(billerId));
    }

    [Fact]
    public void ForBillerAddsMarker()
    {
        Assert.Equal("preview-abc123", PreviewTenant.ForBiller("abc123"));
        Assert.True(PreviewTenant.IsPreview(PreviewTenant.ForBiller("abc123")));
    }

    [Fact]
    public void ForBillerIsIdempotent()
    {
        var once = PreviewTenant.ForBiller("abc123");
        Assert.Equal(once, PreviewTenant.ForBiller(once));
    }

    [Fact]
    public void LiveBillerIdStripsMarker()
    {
        Assert.Equal("abc123", PreviewTenant.LiveBillerId("preview-abc123"));
        // Inverse of ForBiller.
        Assert.Equal("abc123", PreviewTenant.LiveBillerId(PreviewTenant.ForBiller("abc123")));
    }

    [Fact]
    public void LiveBillerIdLeavesNonPreviewUnchanged()
    {
        Assert.Equal("abc123", PreviewTenant.LiveBillerId("abc123"));
    }
}
