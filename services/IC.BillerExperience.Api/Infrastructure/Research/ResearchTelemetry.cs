using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace IC.BillerExperience.Api.Infrastructure.Research;

internal static class ResearchTelemetry
{
    internal static readonly Counter<long> Requests = BillerExperienceTelemetry.Meter.CreateCounter<long>("ic.biller.research.requests");
    internal static readonly Counter<long> Failures = BillerExperienceTelemetry.Meter.CreateCounter<long>("ic.biller.research.failures");
    internal static readonly Counter<long> Pages = BillerExperienceTelemetry.Meter.CreateCounter<long>("ic.biller.research.pages");
    internal static readonly Histogram<double> Duration = BillerExperienceTelemetry.Meter.CreateHistogram<double>("ic.biller.research.duration", "ms");

    internal static Activity? Start(string host)
    {
        var activity = BillerExperienceTelemetry.Source.StartActivity("biller.website.research", ActivityKind.Client);
        activity?.SetTag("server.address", host);
        activity?.SetTag("research.kind", "same_site");
        return activity;
    }
}
