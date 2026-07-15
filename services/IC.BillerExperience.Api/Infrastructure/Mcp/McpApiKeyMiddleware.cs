using System.Security.Cryptography;
using System.Text;
using IC.BillerExperience.Api.Configuration;
using Microsoft.Extensions.Options;

namespace IC.BillerExperience.Api.Infrastructure.Mcp;

public sealed partial class McpApiKeyMiddleware(
    RequestDelegate next,
    IOptions<BillerExperienceOptions> options,
    ILogger<McpApiKeyMiddleware> logger)
{
    private readonly McpOptions _options = options.Value.Mcp;

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/mcp"))
        {
            await next(context);
            return;
        }

        if (!_options.Enabled)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var supplied = context.Request.Headers["X-IC-MCP-Key"].ToString();
        if (_options.ApiKey.Length < 32 || supplied.Length != _options.ApiKey.Length ||
            !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(supplied),
                Encoding.UTF8.GetBytes(_options.ApiKey)))
        {
            LogUnauthorized(logger, context.Connection.RemoteIpAddress?.ToString(), context.TraceIdentifier);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        await next(context);
    }

    [LoggerMessage(2791, LogLevel.Error, "Unauthorized MCP request from {RemoteAddress}; request {RequestId}")]
    private static partial void LogUnauthorized(ILogger logger, string? remoteAddress, string requestId);
}
