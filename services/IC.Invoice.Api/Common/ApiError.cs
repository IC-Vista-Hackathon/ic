namespace IC.Invoice.Api.Common;

/// <summary>
/// Error envelope per design/contracts.md: <c>{"error": {"code", "message"}}</c>.
/// Serializes to snake_case via the API's JSON options.
/// </summary>
public sealed record ApiError(ApiErrorBody Error)
{
    public static ApiError Of(string code, string message) => new(new ApiErrorBody(code, message));
}

public sealed record ApiErrorBody(string Code, string Message);
