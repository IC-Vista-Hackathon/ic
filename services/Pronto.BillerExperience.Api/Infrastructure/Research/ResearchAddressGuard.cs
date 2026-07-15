using System.Net;
using System.Net.Sockets;

namespace Pronto.BillerExperience.Api.Infrastructure.Research;

/// <summary>
/// Shared SSRF address safety check used both by the pre-send target validation and by the
/// connection-time guard, so a name that resolves to a safe address during validation cannot
/// be rebound to an internal address at connect time (DNS rebinding).
/// </summary>
public static class ResearchAddressGuard
{
    public static bool IsUnsafe(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) ||
            address.Equals(IPAddress.None) || address.Equals(IPAddress.IPv6None) || address.IsIPv6LinkLocal ||
            address.IsIPv6Multicast || address.IsIPv6SiteLocal)
        {
            return true;
        }

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return (bytes[0] & 0xfe) == 0xfc;
        }

        return bytes[0] is 0 or 10 or 127 ||
               (bytes[0] == 100 && bytes[1] is >= 64 and <= 127) ||
               (bytes[0] == 169 && bytes[1] == 254) ||
               (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
               (bytes[0] == 192 && bytes[1] == 168) ||
               bytes[0] >= 224;
    }
}
