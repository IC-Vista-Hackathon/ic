using System.Net;

namespace Pronto.BillerExperience.Api.Infrastructure.Research;

public interface IDestinationAddressResolver
{
    Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken);
}

public sealed class SystemDestinationAddressResolver : IDestinationAddressResolver
{
    public async Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken) =>
        await Dns.GetHostAddressesAsync(host, cancellationToken);
}
