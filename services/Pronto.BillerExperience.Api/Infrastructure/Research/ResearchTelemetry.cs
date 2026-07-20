using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Pronto.BillerExperience.Api.Infrastructure.Research;

internal static class ResearchTelemetry
{
    internal static readonly Counter<long> Requests = BillerExperienceTelemetry.Meter.CreateCounter<long>("ic.biller.research.requests");
    internal static readonly Counter<long> Failures = BillerExperienceTelemetry.Meter.CreateCounter<long>("ic.biller.research.failures");
    internal static readonly Counter<long> Pages = BillerExperienceTelemetry.Meter.CreateCounter<long>("ic.biller.research.pages");
    internal static readonly Counter<long> Stylesheets = BillerExperienceTelemetry.Meter.CreateCounter<long>("ic.biller.research.stylesheets");
    internal static readonly Counter<long> TruncatedResponses = BillerExperienceTelemetry.Meter.CreateCounter<long>("ic.biller.research.truncated_responses");
    internal static readonly Histogram<long> ResponseBytes = BillerExperienceTelemetry.Meter.CreateHistogram<long>("ic.biller.research.response_bytes", "By");
    internal static readonly Histogram<double> Duration = BillerExperienceTelemetry.Meter.CreateHistogram<double>("ic.biller.research.duration", "ms");

    // Orchestrated (Foundry) research path — the same_site instruments above only cover the local
    // HTTP researcher, so in production these are the only research metrics that fire.
    internal static readonly Counter<long> Coordinations = BillerExperienceTelemetry.Meter.CreateCounter<long>("ic.biller.research.coordinations");
    internal static readonly Histogram<double> CoordinationDuration = BillerExperienceTelemetry.Meter.CreateHistogram<double>("ic.biller.research.coordination.duration", "ms");
    internal static readonly Counter<long> AgentDispatches = BillerExperienceTelemetry.Meter.CreateCounter<long>("ic.biller.research.agent_dispatches");
    internal static readonly Counter<long> AgentExclusions = BillerExperienceTelemetry.Meter.CreateCounter<long>("ic.biller.research.agent_exclusions");
    internal static readonly Histogram<long> AgentNativeCitations = BillerExperienceTelemetry.Meter.CreateHistogram<long>("ic.biller.research.agent_native_citations");

    internal static Activity? Start(string host)
    {
        var activity = BillerExperienceTelemetry.Source.StartActivity("biller.website.research", ActivityKind.Client);
        activity?.SetTag("server.address", host);
        activity?.SetTag("research.kind", "same_site");
        return activity;
    }
}
