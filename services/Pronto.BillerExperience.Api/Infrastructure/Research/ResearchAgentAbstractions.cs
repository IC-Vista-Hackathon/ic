using System.Net;
using System.Net.Sockets;
using Pronto.BillerExperience.Contracts.V1.Research;
using Pronto.Agentic.Orchestration.Abstractions;

namespace Pronto.BillerExperience.Api.Infrastructure.Research;

/// <summary>A provider-neutral seam for a Foundry agent catalog.</summary>
public interface IResearchAgentCatalog
{
    Task<IReadOnlyList<ResearchAgentDescriptor>> ListAsync(CancellationToken cancellationToken);
}

/// <summary>A provider-neutral seam for dispatching work to a catalog agent.</summary>
public interface IResearchAgentDispatcher
{
    Task<BillerResearchResponse> DispatchAsync(
        ResearchAgentDescriptor agent,
        BillerResearchRequest request,
        ResearchAgentInvocationContext? invocationContext,
        CancellationToken cancellationToken);
}

public interface IBillerResearchCoordinator
{
    Task<BillerResearchResponse> ResearchAsync(
        BillerResearchRequest request,
        ResearchExecutionContext? executionContext = null,
        CancellationToken cancellationToken = default);
}

public sealed record ResearchExecutionContext(string BillerId, string RunId, IOrchestrationEventSink ActivitySink);

public sealed record ResearchAgentInvocationContext(
    Uri McpEndpoint,
    string ContextCapabilityToken);

public interface IAgentContextCapabilityIssuer
{
    string Issue(string billerId, string runId, string agentId, bool canWrite);
}

public sealed record ResearchAgentDescriptor(
    string Id,
    string DisplayName,
    IReadOnlySet<string> Capabilities,
    bool Enabled = true,
    bool Approved = true,
    string Provider = "local");

public static class ResearchHttpHandler
{
    /// <summary>
    /// Creates the required primary handler. Automatic redirects must remain disabled so every
    /// redirect destination is resolved and checked before any network request is sent. The
    /// <see cref="SocketsHttpHandler.ConnectCallback"/> re-checks the addresses the socket is
    /// about to connect to, so a host that resolved to a safe address during pre-send validation
    /// cannot be rebound to an internal address at connect time (DNS rebinding).
    /// </summary>
    public static SocketsHttpHandler Create() => new()
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate,
        ConnectCallback = ConnectToValidatedAddressAsync
    };

    private static async ValueTask<Stream> ConnectToValidatedAddressAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host;
        IReadOnlyList<IPAddress> candidates = IPAddress.TryParse(host, out var literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(host, cancellationToken);

        var safe = candidates.Where(address => !ResearchAddressGuard.IsUnsafe(address)).ToArray();
        if (safe.Length == 0)
        {
            throw new HttpRequestException("research.unsafe_target");
        }

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(safe, context.DnsEndPoint.Port, cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}

/// <summary>Allows the hardened same-site reader to participate as a coordinator worker.</summary>
public sealed class SameSiteResearchAgentDispatcher(IBillerWebsiteResearcher researcher) : IResearchAgentDispatcher
{
    public Task<BillerResearchResponse> DispatchAsync(
        ResearchAgentDescriptor agent,
        BillerResearchRequest request,
        ResearchAgentInvocationContext? invocationContext,
        CancellationToken cancellationToken) => researcher.ResearchAsync(request, cancellationToken);
}

public sealed class LocalResearchAgentCatalog : IResearchAgentCatalog
{
    private static readonly IReadOnlyList<ResearchAgentDescriptor> Agents =
    [
        new("same-site-research", "Same-site Research", new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "biller_research"
        })
    ];

    public Task<IReadOnlyList<ResearchAgentDescriptor>> ListAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Agents);
    }
}
