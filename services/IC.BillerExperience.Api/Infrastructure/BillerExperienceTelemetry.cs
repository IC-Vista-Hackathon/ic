using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace IC.BillerExperience.Api.Infrastructure;

public static class BillerExperienceTelemetry
{
    public const string SourceName = "IC.BillerExperience";
    public const string MeterName = "IC.BillerExperience";

    public static readonly ActivitySource Source = new(SourceName);
    public static readonly Meter Meter = new(MeterName);
    public static readonly Counter<long> ChatTurns = Meter.CreateCounter<long>("ic.biller.chat.turns");
    public static readonly Counter<long> StateTransitions = Meter.CreateCounter<long>("ic.biller.workflow.state.transitions");
    public static readonly Counter<long> ValidationFailures = Meter.CreateCounter<long>("ic.biller.validation.failures");
    public static readonly Counter<long> ModelCalls = Meter.CreateCounter<long>("ic.biller.model.calls");
    public static readonly Counter<long> PersistenceOperations = Meter.CreateCounter<long>("ic.biller.persistence.operations");
    public static readonly Histogram<double> ModelDuration = Meter.CreateHistogram<double>("ic.biller.model.duration", "ms");
    public static readonly Histogram<double> PersistenceDuration = Meter.CreateHistogram<double>("ic.biller.persistence.duration", "ms");
}
