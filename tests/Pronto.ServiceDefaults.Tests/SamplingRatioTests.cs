using Pronto.ServiceDefaults;
using Xunit;

namespace Pronto.ServiceDefaults.Tests;

public class SamplingRatioTests
{
    [Theory]
    [InlineData("1", 1.0f)]
    [InlineData("1.0", 1.0f)]
    [InlineData("0.5", 0.5f)]
    [InlineData("0.25", 0.25f)]
    [InlineData("0.001", 0.001f)]
    [InlineData("  0.5  ", 0.5f)] // surrounding whitespace tolerated
    public void ValidRatioIsUsed(string configured, float expected)
    {
        Assert.Equal(expected, ServiceDefaultsExtensions.ResolveSamplingRatio(configured), precision: 6);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-number")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("0")]      // zero would disable telemetry
    [InlineData("-0.5")]   // negative
    [InlineData("1.5")]    // above 1
    [InlineData("100")]
    public void InvalidOrOutOfRangeFallsBackToKeepEverything(string? configured)
    {
        Assert.Equal(1.0f, ServiceDefaultsExtensions.ResolveSamplingRatio(configured));
    }

    [Fact]
    public void NaNNeverReachesSampler()
    {
        // Regression: float.TryParse("NaN") succeeds, and NaN slips a `<= 0 || > 1` guard because
        // every NaN comparison is false. The positive range test must reject it.
        Assert.False(float.IsNaN(ServiceDefaultsExtensions.ResolveSamplingRatio("NaN")));
    }
}
