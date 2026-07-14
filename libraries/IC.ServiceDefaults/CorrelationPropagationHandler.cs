using System.Diagnostics;

namespace IC.ServiceDefaults;

public sealed class CorrelationPropagationHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var activity = Activity.Current;
        var correlationId = activity?.GetTagItem("ic.correlation_id")?.ToString();
        var billerId = activity?.GetTagItem("ic.biller_id")?.ToString();
        if (!string.IsNullOrWhiteSpace(correlationId)) request.Headers.TryAddWithoutValidation(RequestObservabilityMiddleware.CorrelationHeader, correlationId);
        if (!string.IsNullOrWhiteSpace(billerId)) request.Headers.TryAddWithoutValidation(RequestObservabilityMiddleware.BillerHeader, billerId);
        return base.SendAsync(request, cancellationToken);
    }
}
