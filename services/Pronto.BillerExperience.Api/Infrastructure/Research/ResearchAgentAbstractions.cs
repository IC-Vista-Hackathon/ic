using System.Net;
using System.Net.Sockets;
using Pronto.BillerExperience.Contracts.V1.AgentContext;
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

/// <summary>
/// Carries the per-turn execution identity separately from the persisted shared-context run.
/// Activity uses <see cref="ExecutionId"/>; MCP capabilities use <see cref="ContextRunId"/>.
/// </summary>
public sealed record ResearchExecutionContext(
    string BillerId,
    string ExecutionId,
    string ContextRunId,
    IOrchestrationEventSink ActivitySink);

public sealed record ResearchAgentInvocationContext(AgentContextSnapshot SharedContext);

public interface IAgentContextCapabilityIssuer
{
    string Issue(string billerId, string runId, string agentId, bool canWrite);
}

/// <summary>
/// Gives orchestration access to shared context through MCP. Capability tokens stay behind this
/// boundary and are never placed in model-visible prompts.
/// </summary>
public interface IAgentContextMcpGateway
{
    Task<AgentContextSnapshot> GetAsync(string capabilityToken, CancellationToken cancellationToken);

    Task<AgentContextSnapshot> AppendAsync(
        string capabilityToken,
        AppendAgentContextRequest request,
        CancellationToken cancellationToken);
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
        CancellationToken cancellationToken) => request.Website is null
            ? Task.FromResult(new BillerResearchResponse(
                ResearchOutcome.Skipped, [], [], ["research.website_missing"], "research.website_missing"))
            : researcher.ResearchAsync(request, cancellationToken);
}

public sealed class LocalResearchAgentCatalog : IResearchAgentCatalog
{
    public static readonly ResearchAgentDescriptor SameSiteAgent =
        new("same-site-research", "Same-site Research", new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "biller_research"
        });

    private static readonly IReadOnlyList<ResearchAgentDescriptor> Agents = [SameSiteAgent];

    public Task<IReadOnlyList<ResearchAgentDescriptor>> ListAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Agents);
    }
}

/// <summary>
/// Keeps deterministic first-party extraction in the production pool while adding Foundry's
/// specialized web-search agents. The local worker is first so a catalog cap cannot exclude it.
/// </summary>
public sealed class CompositeResearchAgentCatalog(FoundryResearchAgentAdapter foundry) : IResearchAgentCatalog
{
    public async Task<IReadOnlyList<ResearchAgentDescriptor>> ListAsync(CancellationToken cancellationToken)
    {
        var agents = await foundry.ListAsync(cancellationToken);
        return new[] { LocalResearchAgentCatalog.SameSiteAgent }
            .Concat(agents.Where(agent => !agent.Id.Equals(
                LocalResearchAgentCatalog.SameSiteAgent.Id,
                StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }
}

public sealed class CompositeResearchAgentDispatcher(
    SameSiteResearchAgentDispatcher sameSite,
    FoundryResearchAgentAdapter foundry) : IResearchAgentDispatcher
{
    public Task<BillerResearchResponse> DispatchAsync(
        ResearchAgentDescriptor agent,
        BillerResearchRequest request,
        ResearchAgentInvocationContext? invocationContext,
        CancellationToken cancellationToken) =>
        agent.Id.Equals(LocalResearchAgentCatalog.SameSiteAgent.Id, StringComparison.OrdinalIgnoreCase)
            ? sameSite.DispatchAsync(agent, request, invocationContext, cancellationToken)
            : foundry.DispatchAsync(agent, request, invocationContext, cancellationToken);
}
