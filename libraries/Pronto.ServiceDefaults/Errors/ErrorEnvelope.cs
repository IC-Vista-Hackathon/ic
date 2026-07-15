namespace Pronto.ServiceDefaults.Errors;

/// <summary>Wire shape for every error: {"error":{"code","message"}} per design/contracts.md.</summary>
public sealed record ErrorEnvelope(ErrorDetail Error);

public sealed record ErrorDetail(string Code, string Message);
