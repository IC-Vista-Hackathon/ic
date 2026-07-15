using IC.BillerExperience.Api.Domain;
using IC.BillerExperience.Api.Infrastructure.Persistence;
using IC.BillerExperience.Contracts.V1.AgentContext;
using System.Text.RegularExpressions;

namespace IC.BillerExperience.Api.Application;

public sealed partial class AgentContextService(
    IBillerExperienceRepository repository,
    ILogger<AgentContextService> logger)
{
    private const int MaxEntries = 200;
    private const int MaxContentLength = 4_000;

    public async ValueTask<AgentContextSnapshot> EnsureAsync(
        string billerId,
        string runId,
        string goal,
        CancellationToken cancellationToken)
    {
        var existing = await repository.GetAgentContextAsync(billerId, runId, cancellationToken);
        if (existing is not null) return Map(existing);

        _ = await repository.GetRunAsync(billerId, runId, cancellationToken)
            ?? throw new KeyNotFoundException($"Onboarding run '{runId}' was not found.");
        var now = DateTimeOffset.UtcNow;
        var created = new AgentContextRecord(
            $"context-{runId}", billerId, runId, "agent_context", 0, goal.Trim(), [], now);
        try
        {
            var saved = await repository.SaveAgentContextAsync(created, null, cancellationToken);
            LogContextCreated(logger, billerId, runId);
            return Map(saved);
        }
        catch (Exception exception)
        {
            LogContextCreateFailed(logger, billerId, runId, exception);
            throw;
        }
    }

    public async ValueTask<AgentContextSnapshot> GetAsync(
        string billerId,
        string runId,
        CancellationToken cancellationToken)
    {
        try
        {
            var context = await repository.GetAgentContextAsync(billerId, runId, cancellationToken)
                ?? throw new KeyNotFoundException($"Shared agent context for run '{runId}' was not found.");
            LogContextRead(logger, billerId, runId, context.Version);
            return Map(context);
        }
        catch (Exception exception)
        {
            LogContextReadFailed(logger, billerId, runId, exception);
            throw;
        }
    }

    public async ValueTask<AgentContextSnapshot> AppendAsync(
        string billerId,
        string runId,
        AppendAgentContextRequest request,
        CancellationToken cancellationToken)
    {
        Validate(request);
        try
        {
            var current = await repository.GetAgentContextAsync(billerId, runId, cancellationToken)
                ?? throw new KeyNotFoundException($"Shared agent context for run '{runId}' was not found.");
            if (current.Version != request.ExpectedVersion)
                throw new ConcurrencyException($"Shared context version is {current.Version}, not {request.ExpectedVersion}.");

            var entry = new AgentContextEntry(
                Guid.NewGuid().ToString("N"), request.Kind, request.AgentId.Trim(), request.Scope.Trim(),
                request.Content.Trim(), request.Sources, request.External, DateTimeOffset.UtcNow);
            var next = current with
            {
                Version = current.Version + 1,
                Entries = current.Entries.Append(entry).TakeLast(MaxEntries).ToArray(),
                UpdatedAt = DateTimeOffset.UtcNow
            };
            var saved = await repository.SaveAgentContextAsync(next, current.ETag, cancellationToken);
            LogContextAppended(logger, billerId, runId, entry.AgentId, entry.Kind.ToString(), saved.Version);
            return Map(saved);
        }
        catch (Exception exception)
        {
            LogContextAppendFailed(logger, billerId, runId, request.AgentId, exception);
            throw;
        }
    }

    private static void Validate(AppendAgentContextRequest request)
    {
        if (request.ExpectedVersion < 0) throw new ArgumentOutOfRangeException(nameof(request), "Expected version cannot be negative.");
        if (string.IsNullOrWhiteSpace(request.AgentId) || request.AgentId.Length > 200)
            throw new ArgumentException("Agent ID is required and must not exceed 200 characters.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Scope) || request.Scope.Length > 100)
            throw new ArgumentException("Context scope is required and must not exceed 100 characters.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Content) || request.Content.Length > MaxContentLength)
            throw new ArgumentException($"Context content is required and must not exceed {MaxContentLength} characters.", nameof(request));
        if (request.Sources.Count > 20 || request.Sources.Any(source => source.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("Context sources must contain at most 20 absolute HTTPS URLs.", nameof(request));
        if (request.External && request.Sources.Count == 0)
            throw new ArgumentException("External observations require at least one HTTPS source.", nameof(request));
        if (LooksSensitive(request.Content))
            throw new ArgumentException("Context content appears to contain credentials or payment instrument data.", nameof(request));
    }

    private static bool LooksSensitive(string content)
    {
        return PaymentInstrumentPattern().Matches(content)
                   .Select(match => new string(match.Value.Where(char.IsDigit).ToArray()))
                   .Any(IsLuhnValid) ||
               content.Contains("Bearer ", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("api_key", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("client_secret", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("password=", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLuhnValid(string digits)
    {
        if (digits.Length is < 13 or > 19) return false;
        var sum = 0;
        var alternate = false;
        for (var index = digits.Length - 1; index >= 0; index--)
        {
            var value = digits[index] - '0';
            if (alternate && (value *= 2) > 9) value -= 9;
            sum += value;
            alternate = !alternate;
        }
        return sum % 10 == 0;
    }

    [GeneratedRegex(@"(?<!\d)(?:\d[ -]?){13,19}(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex PaymentInstrumentPattern();

    private static AgentContextSnapshot Map(AgentContextRecord context) =>
        new(context.BillerId, context.RunId, context.Version, context.Goal, context.Entries, context.UpdatedAt);

    [LoggerMessage(2700, LogLevel.Information, "Created shared agent context for biller {BillerId}, run {RunId}")]
    private static partial void LogContextCreated(ILogger logger, string billerId, string runId);
    [LoggerMessage(2701, LogLevel.Information, "Read shared agent context for biller {BillerId}, run {RunId}, version {Version}")]
    private static partial void LogContextRead(ILogger logger, string billerId, string runId, long version);
    [LoggerMessage(2702, LogLevel.Information, "Agent {AgentId} appended {Kind} to biller {BillerId}, run {RunId}, version {Version}")]
    private static partial void LogContextAppended(ILogger logger, string billerId, string runId, string agentId, string kind, long version);
    [LoggerMessage(2797, LogLevel.Error, "Creating shared agent context for biller {BillerId}, run {RunId} failed")]
    private static partial void LogContextCreateFailed(ILogger logger, string billerId, string runId, Exception exception);
    [LoggerMessage(2798, LogLevel.Error, "Reading shared agent context for biller {BillerId}, run {RunId} failed")]
    private static partial void LogContextReadFailed(ILogger logger, string billerId, string runId, Exception exception);
    [LoggerMessage(2799, LogLevel.Error, "Agent {AgentId} failed to append shared context for biller {BillerId}, run {RunId}")]
    private static partial void LogContextAppendFailed(ILogger logger, string billerId, string runId, string agentId, Exception exception);
}
