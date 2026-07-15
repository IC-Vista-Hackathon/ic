using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Pronto.ServiceDefaults;

public sealed partial class RequestObservabilityMiddleware(RequestDelegate next, ILogger<RequestObservabilityMiddleware> logger)
{
    public const string CorrelationHeader = "x-correlation-id";
    public const string BillerHeader = "x-ic-biller-id";

    public async Task InvokeAsync(HttpContext context)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var correlationId = context.Request.Headers[CorrelationHeader].FirstOrDefault() ?? Guid.NewGuid().ToString("N");
        var billerId = context.Request.Headers[BillerHeader].FirstOrDefault();
        context.Response.Headers[CorrelationHeader] = correlationId;
        Activity.Current?.SetTag("ic.correlation_id", correlationId);
        if (!string.IsNullOrWhiteSpace(billerId)) Activity.Current?.SetTag("ic.biller_id", billerId);
        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["trace_id"] = Activity.Current?.TraceId.ToString(),
            ["span_id"] = Activity.Current?.SpanId.ToString(),
            ["correlation_id"] = correlationId,
            ["biller_id"] = billerId
        });
        LogRequestStarted(logger, context.Request.Method, context.Request.Path, correlationId, Activity.Current?.TraceId.ToString());
        await next(context);
        var elapsed = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
        if (context.Response.StatusCode >= 400)
        {
            LogRequestFailed(logger, context.Request.Method, context.Request.Path, context.Response.StatusCode, elapsed, correlationId, Activity.Current?.TraceId.ToString());
        }
        else
        {
            LogRequestCompleted(logger, context.Request.Method, context.Request.Path, context.Response.StatusCode, elapsed, correlationId, Activity.Current?.TraceId.ToString());
        }
    }

    [LoggerMessage(10, LogLevel.Information, "HTTP {Method} {Path} started; correlation {CorrelationId}, trace {TraceId}")]
    private static partial void LogRequestStarted(ILogger logger, string method, PathString path, string correlationId, string? traceId);
    [LoggerMessage(11, LogLevel.Information, "HTTP {Method} {Path} completed {StatusCode} in {ElapsedMs:F1} ms; correlation {CorrelationId}, trace {TraceId}")]
    private static partial void LogRequestCompleted(ILogger logger, string method, PathString path, int statusCode, double elapsedMs, string correlationId, string? traceId);
    [LoggerMessage(12, LogLevel.Error, "HTTP {Method} {Path} failed {StatusCode} in {ElapsedMs:F1} ms; correlation {CorrelationId}, trace {TraceId}")]
    private static partial void LogRequestFailed(ILogger logger, string method, PathString path, int statusCode, double elapsedMs, string correlationId, string? traceId);
}
