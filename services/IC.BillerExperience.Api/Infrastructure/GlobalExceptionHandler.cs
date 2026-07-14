using System.Diagnostics;
using IC.BillerExperience.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace IC.BillerExperience.Api.Infrastructure;

public sealed partial class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var traceId = Activity.Current?.TraceId.ToString() ?? httpContext.TraceIdentifier;
        LogRequestError(logger, httpContext.Request.Method, httpContext.Request.Path, traceId, exception);

        var (status, title, code) = exception switch
        {
            KeyNotFoundException => (StatusCodes.Status404NotFound, "Resource not found", "resource_not_found"),
            ArgumentException => (StatusCodes.Status400BadRequest, "Invalid request", "invalid_request"),
            ConcurrencyException => (StatusCodes.Status409Conflict, "Concurrent update", "concurrent_update"),
            _ => (StatusCodes.Status500InternalServerError, "Unexpected error", "unexpected_error")
        };

        httpContext.Response.StatusCode = status;
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = status == StatusCodes.Status500InternalServerError ? "The request could not be completed." : exception.Message,
            Extensions = { ["code"] = code, ["trace_id"] = traceId }
        };
        try
        {
            await httpContext.Response.WriteAsJsonAsync(
                problem,
                options: null,
                contentType: "application/problem+json",
                cancellationToken);
            return true;
        }
        catch (Exception writeException)
        {
            LogProblemResponseError(logger, httpContext.Request.Method, httpContext.Request.Path, traceId, writeException);
            throw;
        }
    }

    [LoggerMessage(2000, LogLevel.Error, "Request {Method} {Path} failed; trace {TraceId}")]
    private static partial void LogRequestError(ILogger logger, string method, string path, string traceId, Exception exception);

    [LoggerMessage(2001, LogLevel.Error, "Writing the problem response for {Method} {Path} failed; trace {TraceId}")]
    private static partial void LogProblemResponseError(ILogger logger, string method, string path, string traceId, Exception exception);
}
