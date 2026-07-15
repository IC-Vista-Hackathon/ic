using System.Net;
using Pronto.BillerExperience.Api.Infrastructure.Research;
using Xunit;

namespace Pronto.BillerExperience.Api.Tests;

/// <summary>
/// The guard is applied both at pre-send target validation and at socket connect time, so a host
/// that resolves to a safe address during validation but rebinds to an internal address at connect
/// (DNS rebinding) is still rejected. These cases pin the classifier the connect-time check relies on.
/// </summary>
public sealed class ResearchAddressGuardTests
{
    [Theory]
    [InlineData("127.0.0.1")]        // loopback
    [InlineData("10.0.0.5")]         // private class A
    [InlineData("172.16.9.9")]       // private class B
    [InlineData("192.168.1.1")]      // private class C
    [InlineData("169.254.169.254")]  // link-local / cloud IMDS
    [InlineData("100.64.0.1")]       // CGNAT
    [InlineData("0.0.0.0")]          // unspecified
    [InlineData("224.0.0.1")]        // multicast
    [InlineData("::1")]              // IPv6 loopback
    [InlineData("fc00::1")]          // IPv6 unique local
    [InlineData("fe80::1")]          // IPv6 link-local
    [InlineData("::ffff:10.0.0.5")]  // IPv4-mapped private address
    public void UnsafeAddressesAreRejected(string address) =>
        Assert.True(ResearchAddressGuard.IsUnsafe(IPAddress.Parse(address)));

    [Theory]
    [InlineData("93.184.216.34")]    // public
    [InlineData("8.8.8.8")]          // public
    [InlineData("2606:2800:220:1:248:1893:25c8:1946")] // public IPv6
    public void PublicAddressesAreAllowed(string address) =>
        Assert.False(ResearchAddressGuard.IsUnsafe(IPAddress.Parse(address)));
}
