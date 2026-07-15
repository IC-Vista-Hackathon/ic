using System.Diagnostics;
using Azure.AI.Agents.Persistent;
using Azure.Core;
using Pronto.BillerExperience.Api.Configuration;
using Microsoft.Extensions.Options;

namespace Pronto.BillerExperience.Api.Infrastructure.Research;

public interface IFoundryPersistentAgentGateway
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

/// <summary>Uses the stable Azure AI Persistent Agents SDK against a Foundry project endpoint.</summary>
public sealed partial class FoundryPersistentAgentGateway : IFoundryPersistentAgentGateway
{
    private readonly PersistentAgentsClient _client;
    private readonly TimeSpan _pollInterval;
    private readonly ILogger<FoundryPersistentAgentGateway> _logger;

    public FoundryPersistentAgentGateway(
        IOptions<BillerExperienceOptions> options,
        TokenCredential credential,
        ILogger<FoundryPersistentAgentGateway> logger)
    {
        var research = options.Value.Research;
        if (!Uri.TryCreate(research.FoundryProjectEndpoint, UriKind.Absolute, out var endpoint) ||
            !string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("BillerExperience:Research:FoundryProjectEndpoint must be an absolute HTTPS Foundry project endpoint.");
        }

        _client = new PersistentAgentsClient(endpoint.ToString(), credential);
        _pollInterval = TimeSpan.FromMilliseconds(Math.Clamp(research.FoundryPollIntervalMilliseconds, 100, 5_000));
        _logger = logger;
    }

    public async Task<IReadOnlyList<FoundryAgentDefinition>> ListAgentsAsync(CancellationToken cancellationToken)
    {
        using var activity = BillerExperienceTelemetry.Source.StartActivity("foundry.agents.list");
        var agents = new List<FoundryAgentDefinition>();
        await foreach (var agent in _client.Administration
                           .GetAgentsAsync(limit: 100, cancellationToken: cancellationToken))
        {
            agents.Add(new FoundryAgentDefinition(
                agent.Id,
                string.IsNullOrWhiteSpace(agent.Name) ? agent.Id : agent.Name,
                agent.Metadata));
        }

        activity?.SetTag("foundry.agent.count", agents.Count);
        return agents;
    }

    public async Task<FoundryAgentOutput> InvokeAsync(
        string agentId,
        string prompt,
        CancellationToken cancellationToken)
    {
        using var activity = BillerExperienceTelemetry.Source.StartActivity("foundry.agent.invoke");
        activity?.SetTag("gen_ai.agent.id", agentId);
        PersistentAgentThread? thread = null;
        try
        {
            var agent = (await _client.Administration.GetAgentAsync(agentId, cancellationToken)).Value;
            thread = (await _client.Threads.CreateThreadAsync(cancellationToken: cancellationToken)).Value;
            await _client.Messages.CreateMessageAsync(
                thread.Id,
                MessageRole.User,
                prompt,
                cancellationToken: cancellationToken);

            var run = (await _client.Runs.CreateRunAsync(thread, agent, cancellationToken)).Value;
            while (run.Status == RunStatus.Queued || run.Status == RunStatus.InProgress)
            {
                await Task.Delay(_pollInterval, cancellationToken);
                run = (await _client.Runs.GetRunAsync(thread.Id, run.Id, cancellationToken)).Value;
            }

            if (run.Status != RunStatus.Completed)
            {
                activity?.SetStatus(ActivityStatusCode.Error, run.Status.ToString());
                throw new FoundryResearchException(
                    "research.foundry_run_failed",
                    $"Foundry run ended with status {run.Status}; provider code {run.LastError?.Code}.",
                    retryable: run.Status == RunStatus.Failed || run.Status == RunStatus.Expired);
            }

            var texts = new List<string>();
            var citations = new List<FoundryCitation>();
            await foreach (var message in _client.Messages.GetMessagesAsync(
                               thread.Id,
                               order: ListSortOrder.Ascending,
                               cancellationToken: cancellationToken))
            {
                if (message.Role != MessageRole.Agent)
                {
                    continue;
                }

                foreach (var content in message.ContentItems.OfType<MessageTextContent>())
                {
                    texts.Add(content.Text);
                    foreach (var citation in content.Annotations.OfType<MessageTextUriCitationAnnotation>())
                    {
                        if (Uri.TryCreate(citation.UriCitation.Uri, UriKind.Absolute, out var uri))
                        {
                            citations.Add(new FoundryCitation(uri, citation.UriCitation.Title));
                        }
                    }
                }
            }

            if (texts.Count == 0)
            {
                throw new FoundryResearchException("research.foundry_empty_output", "Foundry agent returned no text output.", false);
            }

            activity?.SetStatus(ActivityStatusCode.Ok);
            return new FoundryAgentOutput(
                string.Join(Environment.NewLine, texts),
                citations.DistinctBy(citation => citation.Url).ToArray());
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not FoundryResearchException)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "research.foundry_request_failed");
            LogInvocationFailure(_logger, agentId, Activity.Current?.TraceId.ToString(), exception);
            throw new FoundryResearchException("research.foundry_request_failed", "Foundry request failed.", true, exception);
        }
        finally
        {
            if (thread is not null)
            {
                try
                {
                    using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await _client.Threads.DeleteThreadAsync(thread.Id, cleanupTimeout.Token);
                }
                catch (Exception exception)
                {
                    LogThreadCleanupFailure(_logger, thread.Id, Activity.Current?.TraceId.ToString(), exception);
                }
            }
        }
    }

    [LoggerMessage(2670, LogLevel.Error, "Foundry agent {AgentId} invocation failed; trace {TraceId}")]
    private static partial void LogInvocationFailure(ILogger logger, string agentId, string? traceId, Exception exception);

    [LoggerMessage(2671, LogLevel.Error, "Foundry thread {ThreadId} cleanup failed; trace {TraceId}")]
    private static partial void LogThreadCleanupFailure(ILogger logger, string threadId, string? traceId, Exception exception);
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
