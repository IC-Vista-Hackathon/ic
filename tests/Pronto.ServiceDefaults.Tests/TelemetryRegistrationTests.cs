using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using Xunit;

namespace Pronto.ServiceDefaults.Tests;

public class TelemetryRegistrationTests
{
    // Collects the names of every metric the MeterProvider flushes, without pulling in the
    // OpenTelemetry.Exporter.InMemory package.
    private sealed class CollectingExporter : BaseExporter<Metric>
    {
        public List<string> Names { get; } = [];

        public override ExportResult Export(in Batch<Metric> batch)
        {
            foreach (var metric in batch)
            {
                Names.Add(metric.Name);
            }
            return ExportResult.Success;
        }
    }

    // Regression: AddServiceDefaults previously had no way to register a service's own Meter, so a
    // host that relied on it (e.g. Payment.Api) recorded metrics that were never exported. The
    // meters hook must actually reach the MeterProvider.
    [Fact]
    public void RegisteredMeterReachesTheProvider()
    {
        const string meterName = "Pronto.ServiceDefaults.Tests.Registered";
        var exporter = new CollectingExporter();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.AddServiceDefaults("Test.Service", meters: [meterName]);
        builder.Services.ConfigureOpenTelemetryMeterProvider(metrics =>
            metrics.AddReader(new BaseExportingMetricReader(exporter)));

        using var app = builder.Build();
        var meterProvider = app.Services.GetRequiredService<MeterProvider>();

        using var meter = new Meter(meterName);
        meter.CreateCounter<long>("test.registered.count").Add(1);
        meterProvider.ForceFlush();

        Assert.Contains("test.registered.count", exporter.Names);
    }

    // A meter that was NOT registered must not be collected — guards against the hook silently
    // widening collection to every meter in the process.
    [Fact]
    public void UnregisteredMeterIsNotCollected()
    {
        var exporter = new CollectingExporter();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.AddServiceDefaults("Test.Service", meters: ["Pronto.ServiceDefaults.Tests.Only"]);
        builder.Services.ConfigureOpenTelemetryMeterProvider(metrics =>
            metrics.AddReader(new BaseExportingMetricReader(exporter)));

        using var app = builder.Build();
        var meterProvider = app.Services.GetRequiredService<MeterProvider>();

        using var meter = new Meter("Pronto.ServiceDefaults.Tests.Unregistered");
        meter.CreateCounter<long>("test.unregistered.count").Add(1);
        meterProvider.ForceFlush();

        Assert.DoesNotContain("test.unregistered.count", exporter.Names);
    }

    // AddAzureMonitorExporter must be gated on the connection string: with none set it is a no-op
    // and building the host still succeeds (the exporter would otherwise fail without a target).
    [Fact]
    public void AzureMonitorExporterIsSkippedWithoutConnectionString()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.AddServiceDefaults("Test.Service");

        // Building must not throw: with no connection string the Azure Monitor exporter is skipped
        // entirely rather than registered against a missing ingestion target.
        using var app = builder.Build();

        Assert.NotNull(app);
    }
}
