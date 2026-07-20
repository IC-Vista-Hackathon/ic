using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using Microsoft.Extensions.Options;
using System.ClientModel;
using System.ClientModel.Primitives;
using Pronto.BillerExperience.Api.Configuration;
using Pronto.BillerExperience.Api.Infrastructure.Mcp;

#pragma warning disable OPENAI001

namespace Pronto.BillerExperience.Api.Infrastructure.Research;

public sealed record DesiredFoundryAgent(
    string Name,
    string Model,
    string Instructions,
    string Fingerprint,
    string Capability,
    IReadOnlyList<string> AllowedTools,
    bool RuntimeEnabled);
public sealed record ExistingFoundryAgent(
    string Name,
    string? Fingerprint,
    bool HasSharedContextMcp,
    string? Capability,
    bool RuntimeEnabled);

public interface IFoundryAgentAdministrationGateway
{
    Task<IReadOnlyList<ExistingFoundryAgent>> ListAsync(CancellationToken cancellationToken);
    Task CreateVersionAsync(DesiredFoundryAgent agent, CancellationToken cancellationToken);
}

public sealed partial class FoundryAgentReconciler(
    IFoundryAgentAdministrationGateway gateway,
    IOptions<BillerExperienceOptions> options,
    IHostEnvironment environment,
    ILogger<FoundryAgentReconciler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var configuration = options.Value.AgentProvisioning;
        if (!configuration.Enabled) return;
        await ReconcileAsync(stoppingToken);
    }

    public async Task ReconcileAsync(CancellationToken stoppingToken)
    {
        var configuration = options.Value.AgentProvisioning;
        try
        {
            var desired = LoadDesired(configuration, environment.ContentRootPath);
            var existing = (await gateway.ListAsync(stoppingToken)).ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);
            foreach (var agent in desired)
            {
                if (existing.TryGetValue(agent.Name, out var current) &&
                    current.Fingerprint == agent.Fingerprint &&
                    current.HasSharedContextMcp &&
                    string.Equals(current.Capability, agent.Capability, StringComparison.OrdinalIgnoreCase) &&
                    current.RuntimeEnabled == agent.RuntimeEnabled)
                {
                    LogVerified(logger, agent.Name, agent.Fingerprint);
                    continue;
                }
                await gateway.CreateVersionAsync(agent, stoppingToken);
                var verified = (await gateway.ListAsync(stoppingToken)).SingleOrDefault(item => item.Name == agent.Name);
                if (verified?.Fingerprint != agent.Fingerprint ||
                    !verified.HasSharedContextMcp ||
                    !string.Equals(verified.Capability, agent.Capability, StringComparison.OrdinalIgnoreCase) ||
                    verified.RuntimeEnabled != agent.RuntimeEnabled)
                    throw new InvalidOperationException($"Foundry agent '{agent.Name}' was created without its required runtime metadata or shared-context MCP attachment.");
                LogReconciled(logger, agent.Name, agent.Fingerprint);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception exception) { LogFailure(logger, exception); throw; }
    }

    public static IReadOnlyList<DesiredFoundryAgent> LoadDesired(AgentProvisioningOptions options, string contentRoot)
    {
        var root = Path.IsPathRooted(options.DefinitionsPath) ? options.DefinitionsPath : Path.Combine(contentRoot, options.DefinitionsPath);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"Agent definitions path '{root}' was not found.");
        var primary = new HashSet<string>(["onboarding", "financial-planning", "policy", "execution"], StringComparer.OrdinalIgnoreCase);
        var billerResearchWorkers = new HashSet<string>(
            ["biller-research", "biller-brand-research", "biller-payment-policy-research"],
            StringComparer.OrdinalIgnoreCase);
        var activeFoundryAgents = new HashSet<string>(billerResearchWorkers, StringComparer.OrdinalIgnoreCase)
        {
            "research-coordinator"
        };
        return Directory.GetDirectories(root)
            .Select(path => (Name: Path.GetFileName(path), Instructions: Path.Combine(path, "instructions.md")))
            // The grounded compliance agent is provisioned exclusively by the index workflow,
            // which binds its file-search vector store. It must not become a generic MCP agent.
            .Where(item => File.Exists(item.Instructions) &&
                           !string.Equals(item.Name, "compliance", StringComparison.OrdinalIgnoreCase))
            .Select(item =>
            {
                var instructions = File.ReadAllText(item.Instructions);
                var model = primary.Contains(item.Name) ? options.PrimaryModel : options.MiniModel;
                var capability = billerResearchWorkers.Contains(item.Name)
                    ? "biller_research"
                    : item.Name == "research-coordinator"
                        ? "research_consolidation"
                        : item.Name.Replace('-', '_');
                var runtimeEnabled = activeFoundryAgents.Contains(item.Name);
                var allowedTools = LoadAllowedMcpTools(Path.GetDirectoryName(item.Instructions)!);
                var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                    $"{model}\n{instructions}\n{options.McpConnectionId}\n{capability}\n{string.Join(',', allowedTools)}\n{runtimeEnabled}"))).ToLowerInvariant();
                return new DesiredFoundryAgent(
                    item.Name,
                    model,
                    instructions,
                    fingerprint,
                    capability,
                    allowedTools,
                    runtimeEnabled);
            }).ToArray();
    }

    /// <summary>
    /// The MCP tools an agent may call over the shared-context connection: the shared-context tools
    /// every provisioned agent gets, plus the router tools the agent declares in its
    /// <c>tools.json</c> that the MCP server actually exposes. Non-MCP declarations (e.g.
    /// <c>update_config</c>, <c>research_website</c>, file_search) are ignored so an agent is never
    /// granted a tool outside its declared, server-backed set.
    /// </summary>
    public static IReadOnlyList<string> LoadAllowedMcpTools(string agentDirectory)
    {
        var allowed = new List<string>(SharedContextTools);
        var toolsPath = Path.Join(agentDirectory, "tools.json");
        if (!File.Exists(toolsPath)) return allowed;
        using var document = JsonDocument.Parse(File.ReadAllText(toolsPath));
        if (!document.RootElement.TryGetProperty("tools", out var tools) || tools.ValueKind != JsonValueKind.Array)
            return allowed;
        var declaredToolNames = tools.EnumerateArray()
            .Where(tool =>
                tool.TryGetProperty("function", out var function) &&
                function.TryGetProperty("name", out var name) &&
                name.ValueKind == JsonValueKind.String)
            .Select(tool => tool.GetProperty("function").GetProperty("name").GetString());
        foreach (var value in declaredToolNames)
        {
            if (!string.IsNullOrWhiteSpace(value) && McpServerTools.Contains(value) && !allowed.Contains(value))
                allowed.Add(value);
        }
        return allowed;
    }

    private static readonly string[] SharedContextTools = ["get_goal_context", "append_context"];

    private static readonly HashSet<string> McpServerTools = BuildMcpServerTools();

    private static HashSet<string> BuildMcpServerTools()
    {
        var set = new HashSet<string>(SharedContextTools, StringComparer.Ordinal);
        foreach (var descriptor in new ServiceToolRegistry().All)
            set.Add(descriptor.Name);
        return set;
    }

    [LoggerMessage(2680, LogLevel.Information, "Verified Foundry agent {AgentName} at desired fingerprint {Fingerprint} with shared-context MCP")]
    private static partial void LogVerified(ILogger logger, string agentName, string fingerprint);
    [LoggerMessage(2681, LogLevel.Information, "Reconciled Foundry agent {AgentName} to fingerprint {Fingerprint} and verified shared-context MCP")]
    private static partial void LogReconciled(ILogger logger, string agentName, string fingerprint);
    [LoggerMessage(2682, LogLevel.Error, "Foundry agent reconciliation failed")]
    private static partial void LogFailure(ILogger logger, Exception exception);
}

public sealed class FoundryAgentAdministrationGateway(AIProjectClient project, IOptions<BillerExperienceOptions> options) : IFoundryAgentAdministrationGateway
{
    private readonly AgentProvisioningOptions configuration = options.Value.AgentProvisioning;
    private readonly Uri mcpEndpoint = CreateMcpEndpoint(options.Value.Mcp.PublicEndpoint);

    public async Task<IReadOnlyList<ExistingFoundryAgent>> ListAsync(CancellationToken cancellationToken)
    {
        var result = new List<ExistingFoundryAgent>();
        await foreach (var agent in project.AgentAdministrationClient.GetAgentsAsync(limit: 100, cancellationToken: cancellationToken))
        {
            var latest = agent.GetLatestVersion();
            latest.Metadata.TryGetValue("ic.fingerprint", out var fingerprint);
            latest.Metadata.TryGetValue("ic.capabilities", out var capability);
            var runtimeEnabled = latest.Metadata.TryGetValue("ic.enabled", out var enabled) &&
                                 bool.TryParse(enabled, out var parsedEnabled) && parsedEnabled;
            var hasMcp = latest.Definition is DeclarativeAgentDefinition definition && definition.Tools.Any(IsRequiredMcpTool);
            result.Add(new ExistingFoundryAgent(agent.Name, fingerprint, hasMcp, capability, runtimeEnabled));
        }
        return result;
    }

    public async Task CreateVersionAsync(DesiredFoundryAgent agent, CancellationToken cancellationToken)
    {
        var tools = new List<object>
        {
            new
            {
                type = "mcp",
                server_label = "ic_shared_context",
                server_url = mcpEndpoint.AbsoluteUri,
                project_connection_id = configuration.McpConnectionId,
                allowed_tools = agent.AllowedTools,
                require_approval = "never"
            }
        };
        if (agent.Capability == "biller_research") tools.Add(new { type = "web_search_preview" });
        var payload = BinaryData.FromObjectAsJson(new
        {
            description = "Managed by Pronto agent reconciliation.",
            metadata = new Dictionary<string, string>
            {
                ["ic.fingerprint"] = agent.Fingerprint,
                ["ic.approved"] = "true",
                ["ic.enabled"] = agent.RuntimeEnabled.ToString().ToLowerInvariant(),
                ["ic.capabilities"] = agent.Capability,
                ["ic.runtime"] = agent.RuntimeEnabled ? "foundry" : "definition_only",
                ["ic.mcp.required"] = "ic-shared-context-mcp"
            },
            definition = new
            {
                kind = "prompt",
                model = agent.Model,
                instructions = agent.Instructions,
                tools
            }
        });
        await project.AgentAdministrationClient.CreateAgentVersionAsync(
            agent.Name,
            BinaryContent.Create(payload),
            foundryFeatures: null,
            new RequestOptions { CancellationToken = cancellationToken });
    }

    private bool IsRequiredMcpTool(object tool)
    {
        var connection = tool.GetType().GetProperty("ProjectConnectionId")?.GetValue(tool) as string;
        var label = tool.GetType().GetProperty("ServerLabel")?.GetValue(tool) as string;
        var serverUri = tool.GetType().GetProperty("ServerUri")?.GetValue(tool) as Uri;
        if (IsExpectedConnection(connection) && IsExpectedServer(serverUri?.AbsoluteUri) &&
            string.Equals(label, "ic_shared_context", StringComparison.OrdinalIgnoreCase))
            return true;

        try
        {
            using var document = JsonDocument.Parse(ModelReaderWriter.Write(tool, ModelReaderWriterOptions.Json));
            var root = document.RootElement;
            var serializedConnection = root.TryGetProperty("project_connection_id", out var connectionProperty)
                ? connectionProperty.GetString()
                : null;
            var serializedLabel = root.TryGetProperty("server_label", out var labelProperty)
                ? labelProperty.GetString()
                : null;
            var serializedServer = root.TryGetProperty("server_url", out var serverProperty)
                ? serverProperty.GetString()
                : null;
            return IsExpectedConnection(serializedConnection) && IsExpectedServer(serializedServer) &&
                   string.Equals(serializedLabel, "ic_shared_context", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private bool IsExpectedConnection(string? connection) =>
        string.Equals(connection, configuration.McpConnectionId, StringComparison.OrdinalIgnoreCase) ||
        connection?.EndsWith($"/{configuration.McpConnectionId}", StringComparison.OrdinalIgnoreCase) == true;

    private bool IsExpectedServer(string? server) =>
        Uri.TryCreate(server, UriKind.Absolute, out var uri) &&
        Uri.Compare(uri, mcpEndpoint, UriComponents.HttpRequestUrl, UriFormat.SafeUnescaped,
            StringComparison.OrdinalIgnoreCase) == 0;

    private static Uri CreateMcpEndpoint(string endpoint) =>
        Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? uri
            : throw new InvalidOperationException(
                "BillerExperience:Mcp:PublicEndpoint must be an absolute HTTP or HTTPS endpoint when Foundry agent provisioning is enabled.");
}
