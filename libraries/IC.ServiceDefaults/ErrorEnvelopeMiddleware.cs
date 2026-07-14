using IC.ServiceDefaults.Errors;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace IC.ServiceDefaults;

/// <summary>Converts ServiceException (and anything else) into the standard error envelope.</summary>
public sealed partial class ErrorEnvelopeMiddleware
{
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
        catch (ServiceException exception)
        {
            context.Response.StatusCode = exception.StatusCode;
            await context.Response
                .WriteAsJsonAsync(new ErrorEnvelope(new ErrorDetail(exception.Code, exception.Message)))
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (!context.Response.HasStarted)
        {
            LogUnhandledException(logger, exception, context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response
                .WriteAsJsonAsync(new ErrorEnvelope(new ErrorDetail("internal_error", "An unexpected error occurred.")))
                .ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception for {Path}")]
    private static partial void LogUnhandledException(ILogger logger, Exception exception, PathString path);
}
