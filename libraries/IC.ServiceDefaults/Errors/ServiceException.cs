using Microsoft.AspNetCore.Http;

namespace IC.ServiceDefaults.Errors;

/// <summary>
/// Throw from controllers/stores to produce the standard error envelope with a specific
/// HTTP status. Anything else that escapes becomes a 500 internal_error.
/// </summary>
public sealed class ServiceException : Exception
{
    public ServiceException(int statusCode, string code, string message)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public ServiceException()
    {
    }

    public ServiceException(string message)
        : base(message)
    {
    }

    public ServiceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public int StatusCode { get; } = StatusCodes.Status500InternalServerError;

    public string Code { get; } = "internal_error";

    public static ServiceException NotFound(string code, string message) =>
        new(StatusCodes.Status404NotFound, code, message);

    public static ServiceException Conflict(string code, string message) =>
        new(StatusCodes.Status409Conflict, code, message);

    public static ServiceException BadRequest(string code, string message) =>
        new(StatusCodes.Status400BadRequest, code, message);
}
