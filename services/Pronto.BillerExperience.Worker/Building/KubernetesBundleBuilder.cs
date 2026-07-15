using System.Diagnostics;
using k8s;
using k8s.Autorest;
using k8s.Models;
using Microsoft.Extensions.Options;

namespace Pronto.BillerExperience.Worker.Building;

// Launches the bundle build as a short-lived Kubernetes Job (generate -> build -> validate ->
// publish, all inside the builder image) and waits for it to finish. Runs the heavy Node/Vite/
// Playwright toolchain out-of-process so it never lives in the Worker image.
public sealed partial class KubernetesBundleBuilder(
    IKubernetes client,
    IOptions<BundleBuildOptions> options,
    ILogger<KubernetesBundleBuilder> logger) : IExperienceBundleBuilder
{
    private readonly BundleBuildOptions _options = options.Value;

    public bool Enabled => !string.IsNullOrWhiteSpace(_options.BuilderImage);

    public async ValueTask BuildAsync(BundleBuildRequest request, CancellationToken cancellationToken)
    {
        using var activity = PublicationTelemetry.Source.StartActivity("publication.bundle.build");
        activity?.SetTag("ic.biller_id", request.BillerId);
        activity?.SetTag("ic.revision", request.Revision);

        var job = PayerBundleJobFactory.Create(request, _options);
        var jobName = job.Metadata.Name;
        var ns = _options.Namespace;

        LogBuildStarted(logger, request.Slug, request.Revision, jobName, ns);
        await client.BatchV1.CreateNamespacedJobAsync(job, ns, cancellationToken: cancellationToken);

        try
        {
            await WaitForCompletionAsync(jobName, ns, request, cancellationToken);
            LogBuildSucceeded(logger, request.Slug, request.Revision, jobName);
        }
        finally
        {
            await TryDeleteJobAsync(jobName, ns);
        }
    }

    private async Task WaitForCompletionAsync(
        string jobName,
        string ns,
        BundleBuildRequest request,
        CancellationToken cancellationToken)
    {
        var deadline = Stopwatch.StartNew();
        var budget = TimeSpan.FromSeconds(_options.JobTimeoutSeconds);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var status = (await client.BatchV1.ReadNamespacedJobStatusAsync(jobName, ns, cancellationToken: cancellationToken)).Status;

            if (status?.Succeeded is > 0)
            {
                return;
            }

            if (status?.Failed is > 0)
            {
                var logs = await ReadPodLogsAsync(jobName, ns);
                throw new BundleBuildException(
                    $"Bundle build failed for biller '{request.Slug}' revision '{request.Revision}'.{logs}");
            }

            if (deadline.Elapsed > budget)
            {
                var logs = await ReadPodLogsAsync(jobName, ns);
                throw new BundleBuildException(
                    $"Bundle build for biller '{request.Slug}' revision '{request.Revision}' timed out after {budget.TotalSeconds:n0}s.{logs}");
            }

            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
        }
    }

    // Best-effort log tail for diagnostics; never throws (a failed build must still surface).
    private async Task<string> ReadPodLogsAsync(string jobName, string ns)
    {
        try
        {
            var pods = await client.CoreV1.ListNamespacedPodAsync(ns, labelSelector: $"job-name={jobName}");
            var pod = pods.Items.FirstOrDefault();
            if (pod is null)
            {
                return string.Empty;
            }

            using var stream = await client.CoreV1.ReadNamespacedPodLogAsync(pod.Metadata.Name, ns, tailLines: 40);
            using var reader = new StreamReader(stream);
            var text = await reader.ReadToEndAsync();
            return string.IsNullOrWhiteSpace(text) ? string.Empty : $"\n--- build log (last 40 lines) ---\n{text}";
        }
        catch (Exception exception)
        {
            LogLogReadFailed(logger, jobName, exception);
            return string.Empty;
        }
    }

    private async Task TryDeleteJobAsync(string jobName, string ns)
    {
        try
        {
            await client.BatchV1.DeleteNamespacedJobAsync(
                jobName,
                ns,
                propagationPolicy: "Background");
        }
        catch (HttpOperationException exception)
        {
            LogJobDeleteFailed(logger, jobName, exception);
        }
    }

    [LoggerMessage(1300, LogLevel.Information, "Launching bundle build Job {JobName} in {Namespace} for biller {Slug} revision {Revision}")]
    private static partial void LogBuildStarted(ILogger logger, string slug, string revision, string jobName, string @namespace);

    [LoggerMessage(1301, LogLevel.Information, "Bundle build Job {JobName} succeeded for biller {Slug} revision {Revision}")]
    private static partial void LogBuildSucceeded(ILogger logger, string slug, string revision, string jobName);

    [LoggerMessage(1302, LogLevel.Warning, "Could not read build logs for Job {JobName}")]
    private static partial void LogLogReadFailed(ILogger logger, string jobName, Exception exception);

    [LoggerMessage(1303, LogLevel.Warning, "Could not delete finished build Job {JobName}")]
    private static partial void LogJobDeleteFailed(ILogger logger, string jobName, Exception exception);
}

// Distinct type so the processor can classify build failures separately from artifact failures.
public sealed class BundleBuildException(string message) : Exception(message);
