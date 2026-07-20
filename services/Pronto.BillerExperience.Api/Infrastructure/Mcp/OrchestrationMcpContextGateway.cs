using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Pronto.BillerExperience.Api.Configuration;
using Pronto.BillerExperience.Api.Infrastructure.Research;
using Pronto.BillerExperience.Contracts.V1.AgentContext;

namespace Pronto.BillerExperience.Api.Infrastructure.Mcp;

/// <summary>
/// Calls the public MCP transport on behalf of orchestration so credentials and scoped capability
/// tokens never cross the model boundary.
/// </summary>
public sealed partial class OrchestrationMcpContextGateway(
    HttpClient httpClient,
    IOptions<BillerExperienceOptions> options,
    ILoggerFactory loggerFactory,
    ILogger<OrchestrationMcpContextGateway> logger) : IAgentContextMcpGateway, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly McpOptions _options = options.Value.Mcp;
    private readonly SemaphoreSlim _clientGate = new(1, 1);
    private HttpClientTransport? _transport;
    private McpClient? _client;

    public Task<AgentContextSnapshot> GetAsync(
        string capabilityToken,
        CancellationToken cancellationToken) => InvokeAsync(
            "get_goal_context",
            new Dictionary<string, object?> { ["capabilityToken"] = capabilityToken },
            cancellationToken);

    public Task<AgentContextSnapshot> AppendAsync(
        string capabilityToken,
        AppendAgentContextRequest request,
        CancellationToken cancellationToken) => InvokeAsync(
            "append_context",
            new Dictionary<string, object?>
            {
                ["capabilityToken"] = capabilityToken,
                ["expectedVersion"] = request.ExpectedVersion,
                ["kind"] = request.Kind.ToString(),
                ["scope"] = request.Scope,
                ["content"] = request.Content,
                ["sources"] = request.Sources.Select(source => source.AbsoluteUri).ToArray(),
                ["external"] = request.External
            },
            cancellationToken);

    private async Task<AgentContextSnapshot> InvokeAsync(
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            var client = await GetClientAsync(cancellationToken);
            var result = await client.CallToolAsync(
                toolName,
                arguments,
                cancellationToken: cancellationToken);
            if (result.IsError == true)
            {
                throw new InvalidOperationException($"MCP tool '{toolName}' returned an error result.");
            }

            var structured = result.StructuredContent
                ?? throw new InvalidOperationException($"MCP tool '{toolName}' returned no structured context.");
            var snapshot = DeserializeSnapshot(structured, toolName);
            LogCompleted(logger, toolName, snapshot.BillerId, snapshot.RunId, snapshot.Version,
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds, Activity.Current?.TraceId.ToString());
            return snapshot;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogFailed(logger, toolName, Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
                Activity.Current?.TraceId.ToString(), exception);
            throw;
        }
    }

    private async Task<McpClient> GetClientAsync(CancellationToken cancellationToken)
    {
        if (_client is not null)
        {
            return _client;
        }

        await _clientGate.WaitAsync(cancellationToken);
        try
        {
            if (_client is not null)
            {
                return _client;
            }

            var transportOptions = new HttpClientTransportOptions
            {
                Endpoint = new Uri(_options.PublicEndpoint),
                Name = "ic-orchestration-context",
                TransportMode = HttpTransportMode.StreamableHttp,
                AdditionalHeaders = new Dictionary<string, string>
                {
                    ["X-IC-MCP-Key"] = _options.ApiKey
                }
            };
            var transport = new HttpClientTransport(
                transportOptions,
                httpClient,
                loggerFactory,
                ownsHttpClient: false);
            try
            {
                var client = await McpClient.CreateAsync(
                    transport,
                    loggerFactory: loggerFactory,
                    cancellationToken: cancellationToken);
                _transport = transport;
                _client = client;
                return client;
            }
            catch
            {
                await transport.DisposeAsync();
                throw;
            }
        }
        finally
        {
            _clientGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _clientGate.WaitAsync();
        try
        {
            if (_client is not null)
            {
                await _client.DisposeAsync();
                _client = null;
            }
            if (_transport is not null)
            {
                await _transport.DisposeAsync();
                _transport = null;
            }
        }
        finally
        {
            _clientGate.Release();
            _clientGate.Dispose();
        }
    }

    internal static AgentContextSnapshot DeserializeSnapshot(object structured, string toolName = "context") =>
        JsonSerializer.Deserialize<AgentContextSnapshot>(
            JsonSerializer.Serialize(structured, JsonOptions),
            JsonOptions)
        ?? throw new JsonException($"MCP tool '{toolName}' returned an empty context snapshot.");

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        jsonOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return jsonOptions;
    }

    [LoggerMessage(2760, LogLevel.Information, "Orchestration MCP {ToolName} completed for biller {BillerId}, run {RunId}, version {Version} in {ElapsedMs:F1} ms; trace {TraceId}")]
    private static partial void LogCompleted(ILogger logger, string toolName, string billerId, string runId, long version, double elapsedMs, string? traceId);

    [LoggerMessage(2794, LogLevel.Error, "Orchestration MCP {ToolName} failed after {ElapsedMs:F1} ms; trace {TraceId}")]
    private static partial void LogFailed(ILogger logger, string toolName, double elapsedMs, string? traceId, Exception exception);
}
