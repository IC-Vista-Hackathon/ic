using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Pronto.BillerExperience.Api.Configuration;
using Pronto.BillerExperience.Api.Infrastructure.Research;
using Microsoft.Extensions.Options;

namespace Pronto.BillerExperience.Api.Infrastructure.Mcp;

public sealed partial class AgentContextCapabilityService(
    IOptions<BillerExperienceOptions> options,
    TimeProvider timeProvider,
    ILogger<AgentContextCapabilityService> logger) : IAgentContextCapabilityIssuer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly McpOptions _options = options.Value.Mcp;

    public string Issue(string billerId, string runId, string agentId, bool canWrite)
        => Issue(billerId, runId, agentId, canWrite, payerId: null);

    /// <summary>
    /// Issues a capability bound to the given scope. <paramref name="payerId"/> is set only
    /// after a successful payer-verification handshake (account-number match); it can never be
    /// supplied as a tool argument, so payer-scoped tools trust the bound id, not the caller.
    /// </summary>
    public string Issue(string billerId, string runId, string agentId, bool canWrite, string? payerId)
    {
        try
        {
            EnsureConfigured();
            var claims = new AgentContextCapability(
                billerId,
                runId,
                agentId,
                canWrite,
                timeProvider.GetUtcNow().AddMinutes(Math.Clamp(_options.CapabilityLifetimeMinutes, 1, 120)),
                payerId);
            var payload = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(claims, JsonOptions));
            var signature = Sign(payload);
            LogIssued(logger, billerId, runId, agentId, canWrite, claims.ExpiresAt);
            return $"{payload}.{signature}";
        }
        catch (Exception exception)
        {
            LogIssuanceFailed(logger, billerId, runId, agentId, exception);
            throw;
        }
    }

    public AgentContextCapability Validate(string token, bool writeRequired)
        => Validate(token, writeRequired, payerRequired: false);

    /// <summary>
    /// Validates a capability. When <paramref name="payerRequired"/> is true the token must carry
    /// a payer id bound by the verification handshake; otherwise payer-scoped tools are refused.
    /// </summary>
    public AgentContextCapability Validate(string token, bool writeRequired, bool payerRequired)
    {
        try
        {
            EnsureConfigured();
            var parts = token.Split('.', 2);
            if (parts.Length != 2 || !CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(Sign(parts[0])),
                    Encoding.ASCII.GetBytes(parts[1])))
                throw new UnauthorizedAccessException("The MCP context capability signature is invalid.");

            var claims = JsonSerializer.Deserialize<AgentContextCapability>(Base64UrlDecode(parts[0]), JsonOptions)
                ?? throw new UnauthorizedAccessException("The MCP context capability is invalid.");
            if (claims.ExpiresAt <= timeProvider.GetUtcNow())
                throw new UnauthorizedAccessException("The MCP context capability has expired.");
            if (writeRequired && !claims.CanWrite)
                throw new UnauthorizedAccessException("The MCP context capability is read-only.");
            if (string.IsNullOrWhiteSpace(claims.BillerId) || string.IsNullOrWhiteSpace(claims.RunId) || string.IsNullOrWhiteSpace(claims.AgentId))
                throw new UnauthorizedAccessException("The MCP context capability scope is invalid.");
            if (payerRequired && string.IsNullOrWhiteSpace(claims.PayerId))
                throw new UnauthorizedAccessException("The MCP context capability is not bound to a verified payer.");
            return claims;
        }
        catch (Exception exception) when (exception is not UnauthorizedAccessException)
        {
            LogValidationFailed(logger, exception);
            throw new UnauthorizedAccessException("The MCP context capability is invalid.", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            LogValidationFailed(logger, exception);
            throw;
        }
    }

    private void EnsureConfigured()
    {
        if (_options.CapabilitySigningKey.Length < 32)
            throw new InvalidOperationException("BillerExperience:Mcp:CapabilitySigningKey must contain at least 32 characters.");
    }

    private string Sign(string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_options.CapabilitySigningKey));
        return Base64UrlEncode(hmac.ComputeHash(Encoding.ASCII.GetBytes(payload)));
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        return Convert.FromBase64String(padded);
    }

    [LoggerMessage(2750, LogLevel.Information, "Issued MCP context capability for biller {BillerId}, run {RunId}, agent {AgentId}, writable {CanWrite}, expires {ExpiresAt}")]
    private static partial void LogIssued(ILogger logger, string billerId, string runId, string agentId, bool canWrite, DateTimeOffset expiresAt);
    [LoggerMessage(2790, LogLevel.Error, "MCP context capability validation failed")]
    private static partial void LogValidationFailed(ILogger logger, Exception exception);
    [LoggerMessage(2793, LogLevel.Error, "Issuing MCP context capability failed for biller {BillerId}, run {RunId}, agent {AgentId}")]
    private static partial void LogIssuanceFailed(ILogger logger, string billerId, string runId, string agentId, Exception exception);
}

public sealed record AgentContextCapability(
    string BillerId,
    string RunId,
    string AgentId,
    bool CanWrite,
    DateTimeOffset ExpiresAt,
    string? PayerId = null);
