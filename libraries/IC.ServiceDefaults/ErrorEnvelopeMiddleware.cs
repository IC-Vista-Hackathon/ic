using System.Text.Json;
using System.Diagnostics;
using IC.ServiceDefaults.Errors;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace IC.ServiceDefaults;

/// <summary>Converts ServiceException (and anything else) into the standard error envelope.</summary>
public sealed partial class ErrorEnvelopeMiddleware
{
    // Match the MVC wire policy (snake_case); WriteAsJsonAsync would otherwise use web defaults.
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly RequestDelegate next;
    private readonly ILogger<ErrorEnvelopeMiddleware> logger;

    public ErrorEnvelopeMiddleware(RequestDelegate next, ILogger<ErrorEnvelopeMiddleware> logger)
    {
        this.next = next;
        this.logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context).ConfigureAwait(false);
        }
        catch (ServiceException exception) when (!context.Response.HasStarted)
        {
            LogHandledException(logger, exception, context.Request.Path, exception.StatusCode, exception.Code,
                Activity.Current?.TraceId.ToString(), context.Request.Headers[RequestObservabilityMiddleware.CorrelationHeader].FirstOrDefault());
            context.Response.StatusCode = exception.StatusCode;
            await context.Response
                .WriteAsJsonAsync(new ErrorEnvelope(new ErrorDetail(exception.Code, exception.Message)), Wire)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (!context.Response.HasStarted)
        {
            LogUnhandledException(logger, exception, context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response
                .WriteAsJsonAsync(new ErrorEnvelope(new ErrorDetail("internal_error", "An unexpected error occurred.")), Wire)
                .ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception for {Path}")]
    private static partial void LogUnhandledException(ILogger logger, Exception exception, PathString path);

    [LoggerMessage(101, LogLevel.Error, "Handled service error {Code} ({StatusCode}) for {Path}; correlation {CorrelationId}, trace {TraceId}")]
    private static partial void LogHandledException(ILogger logger, Exception exception, PathString path, int statusCode, string code, string? traceId, string? correlationId);
}
