using System.Diagnostics;
using Azure.AI.Extensions.OpenAI;
using Azure.AI.Projects;
using Azure.Core;
using Pronto.BillerExperience.Api.Configuration;
using Microsoft.Extensions.Options;
using OpenAI.Responses;

#pragma warning disable OPENAI001 // The GA Foundry Agent Service SDK currently exposes Responses API models under this diagnostic.

namespace Pronto.BillerExperience.Api.Infrastructure.Research;

public interface IFoundryAgentServiceGateway
{
    Task<IReadOnlyList<FoundryAgentDefinition>> ListAgentsAsync(CancellationToken cancellationToken);
    Task<FoundryAgentOutput> InvokeAsync(string agentId, string prompt, CancellationToken cancellationToken);
}

public sealed record FoundryAgentDefinition(
    string Id,
    string Name,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record FoundryCitation(Uri Url, string? Title);

public sealed record FoundryAgentOutput(string Text, IReadOnlyList<FoundryCitation> Citations);

/// <summary>Uses the current Foundry Agent Service SDK and Responses API against a Foundry project endpoint.</summary>
public sealed partial class FoundryAgentServiceGateway : IFoundryAgentServiceGateway
{
    private readonly AIProjectClient _project;
    private readonly ILogger<FoundryAgentServiceGateway> _logger;

    public FoundryAgentServiceGateway(
        IOptions<BillerExperienceOptions> options,
        TokenCredential credential,
        ILogger<FoundryAgentServiceGateway> logger)
    {
        var research = options.Value.Research;
        if (!Uri.TryCreate(research.FoundryProjectEndpoint, UriKind.Absolute, out var endpoint) ||
            !string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("BillerExperience:Research:FoundryProjectEndpoint must be an absolute HTTPS Foundry project endpoint.");
        }

        _project = new AIProjectClient(endpoint, credential);
        _logger = logger;
    }

    public async Task<IReadOnlyList<FoundryAgentDefinition>> ListAgentsAsync(CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        using var activity = BillerExperienceTelemetry.Source.StartActivity("foundry.agents.list");
        activity?.SetTag("gen_ai.provider.name", "microsoft.foundry");
        activity?.SetTag("gen_ai.operation.name", "list_agents");
        LogInventoryStarted(_logger, Activity.Current?.TraceId.ToString());
        try
        {
            var agents = new List<FoundryAgentDefinition>();
            await foreach (var agent in _project.AgentAdministrationClient
                               .GetAgentsAsync(limit: 100, cancellationToken: cancellationToken))
            {
                var latest = agent.GetLatestVersion();
                agents.Add(new FoundryAgentDefinition(
                    agent.Id,
                    string.IsNullOrWhiteSpace(agent.Name) ? agent.Id : agent.Name,
                    new Dictionary<string, string>(latest.Metadata, StringComparer.OrdinalIgnoreCase)));
            }

            activity?.SetTag("foundry.agent.count", agents.Count);
            activity?.SetStatus(ActivityStatusCode.Ok);
            LogInventoryCompleted(_logger, agents.Count, Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
                Activity.Current?.TraceId.ToString());
            return agents;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "research.foundry_inventory_failed");
            LogInventoryFailure(_logger, Activity.Current?.TraceId.ToString(), exception);
            throw new FoundryResearchException("research.foundry_inventory_failed", "Foundry agent inventory failed.", true, exception);
        }
    }

    public async Task<FoundryAgentOutput> InvokeAsync(
        string agentId,
        string prompt,
        CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        using var activity = BillerExperienceTelemetry.Source.StartActivity("foundry.agent.invoke");
        activity?.SetTag("gen_ai.agent.id", agentId);
        activity?.SetTag("gen_ai.provider.name", "microsoft.foundry");
        activity?.SetTag("gen_ai.operation.name", "invoke_agent");
        LogInvocationStarted(_logger, agentId, Activity.Current?.TraceId.ToString());
        try
        {
            var responses = _project.ProjectOpenAIClient
                .GetProjectResponsesClientForAgent(new AgentReference(agentId));
            var response = (await responses.CreateResponseAsync(
                prompt,
                previousResponseId: null,
                cancellationToken)).Value;
            var texts = new List<string>();
            var citations = new List<FoundryCitation>();
            foreach (var message in response.OutputItems.OfType<MessageResponseItem>())
            {
                foreach (var content in message.Content)
                {
                    if (!string.IsNullOrWhiteSpace(content.Text)) texts.Add(content.Text);
                    foreach (var citation in content.OutputTextAnnotations.OfType<UriCitationMessageAnnotation>())
                    {
                        citations.Add(new FoundryCitation(citation.Uri, citation.Title));
                    }
                }
            }

            if (texts.Count == 0)
            {
                throw new FoundryResearchException("research.foundry_empty_output", "Foundry agent returned no text output.", false);
            }

            activity?.SetStatus(ActivityStatusCode.Ok);
            LogInvocationCompleted(_logger, agentId, citations.Count,
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds, Activity.Current?.TraceId.ToString());
            return new FoundryAgentOutput(
                string.Join(Environment.NewLine, texts),
                citations.DistinctBy(citation => citation.Url).ToArray());
        }
        catch (FoundryResearchException exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Code);
            LogInvocationFailure(_logger, agentId, exception.Code, Activity.Current?.TraceId.ToString(), exception);
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not FoundryResearchException)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "research.foundry_request_failed");
            LogInvocationFailure(_logger, agentId, "research.foundry_request_failed", Activity.Current?.TraceId.ToString(), exception);
            throw new FoundryResearchException("research.foundry_request_failed", "Foundry request failed.", true, exception);
        }
    }

    [LoggerMessage(2669, LogLevel.Error, "Foundry agent inventory failed; trace {TraceId}")]
    private static partial void LogInventoryFailure(ILogger logger, string? traceId, Exception exception);

    [LoggerMessage(2670, LogLevel.Error, "Foundry agent {AgentId} invocation failed with {ErrorCode}; trace {TraceId}")]
    private static partial void LogInvocationFailure(ILogger logger, string agentId, string errorCode, string? traceId, Exception exception);

    [LoggerMessage(2673, LogLevel.Information, "Listing Foundry agents; trace {TraceId}")]
    private static partial void LogInventoryStarted(ILogger logger, string? traceId);

    [LoggerMessage(2674, LogLevel.Information, "Listed {AgentCount} Foundry agents in {DurationMs} ms; trace {TraceId}")]
    private static partial void LogInventoryCompleted(ILogger logger, int agentCount, double durationMs, string? traceId);

    [LoggerMessage(2675, LogLevel.Information, "Invoking Foundry agent {AgentId}; trace {TraceId}")]
    private static partial void LogInvocationStarted(ILogger logger, string agentId, string? traceId);

    [LoggerMessage(2676, LogLevel.Information, "Foundry agent {AgentId} completed with {CitationCount} citations in {DurationMs} ms; trace {TraceId}")]
    private static partial void LogInvocationCompleted(ILogger logger, string agentId, int citationCount, double durationMs, string? traceId);

}

public sealed class FoundryResearchException : Exception
{
    public FoundryResearchException(string code, string message, bool retryable, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
        Retryable = retryable;
    }

    public string Code { get; }
    public bool Retryable { get; }
}
