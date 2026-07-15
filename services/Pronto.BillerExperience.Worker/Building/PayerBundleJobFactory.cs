using System.Text;
using k8s.Models;

namespace Pronto.BillerExperience.Worker.Building;

// Pure builder for the per-biller bundle Job manifest. Kept free of the Kubernetes client so
// the resulting spec (env wiring, identity, security context, scratch volume) is unit-testable.
public static class PayerBundleJobFactory
{
    public const string ComponentName = "ic-payer-experience-builder";
    private const string WorkVolumeName = "work";
    private const string WorkMountPath = "/work";

    public static V1Job Create(BundleBuildRequest request, BundleBuildOptions options)
    {
        var name = BuildJobName(request.Slug, request.Revision);
        var definitionB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(request.DefinitionJson));

        return new V1Job
        {
            ApiVersion = "batch/v1",
            Kind = "Job",
            Metadata = new V1ObjectMeta
            {
                Name = name,
                Labels = new Dictionary<string, string>
                {
                    ["app.kubernetes.io/name"] = ComponentName,
                    ["app.kubernetes.io/part-of"] = "ic-biller-experience",
                    ["ic.biller-slug"] = Sanitize(request.Slug),
                },
            },
            Spec = new V1JobSpec
            {
                // No in-Job retries: a failed generation/build/validation should surface immediately;
                // the Worker decides whether to retry the whole publication.
                BackoffLimit = 0,
                ActiveDeadlineSeconds = options.ActiveDeadlineSeconds,
                TtlSecondsAfterFinished = options.TtlSecondsAfterFinished,
                Template = new V1PodTemplateSpec
                {
                    Metadata = new V1ObjectMeta
                    {
                        Labels = new Dictionary<string, string>
                        {
                            ["app.kubernetes.io/name"] = ComponentName,
                            ["app.kubernetes.io/part-of"] = "ic-biller-experience",
                            ["azure.workload.identity/use"] = "true",
                        },
                    },
                    Spec = new V1PodSpec
                    {
                        RestartPolicy = "Never",
                        ServiceAccountName = options.ServiceAccountName,
                        SecurityContext = new V1PodSecurityContext
                        {
                            RunAsNonRoot = true,
                            RunAsUser = options.RunAsUser,
                            SeccompProfile = new V1SeccompProfile { Type = "RuntimeDefault" },
                        },
                        Containers =
                        [
                            new V1Container
                            {
                                Name = "builder",
                                Image = options.BuilderImage,
                                Env =
                                [
                                    new V1EnvVar { Name = "PAYER_SLUG", Value = request.Slug },
                                    new V1EnvVar { Name = "PAYER_REVISION", Value = request.Revision },
                                    new V1EnvVar { Name = "PAYER_MODE", Value = options.GeneratorMode },
                                    new V1EnvVar { Name = "PAYER_STORAGE_ENDPOINT", Value = request.StorageEndpoint },
                                    new V1EnvVar { Name = "PAYER_CONTAINER", Value = request.ContainerName },
                                    new V1EnvVar { Name = "PAYER_PUBLISH", Value = "true" },
                                    // The Worker's config publisher owns the atomic active.json flip.
                                    new V1EnvVar { Name = "PAYER_SKIP_ACTIVE", Value = "true" },
                                    new V1EnvVar { Name = "PAYER_DEFINITION_B64", Value = definitionB64 },
                                ],
                                VolumeMounts =
                                [
                                    new V1VolumeMount { Name = WorkVolumeName, MountPath = WorkMountPath },
                                ],
                                Resources = new V1ResourceRequirements
                                {
                                    Requests = new Dictionary<string, ResourceQuantity>
                                    {
                                        ["cpu"] = new("250m"),
                                        ["memory"] = new("512Mi"),
                                    },
                                    Limits = new Dictionary<string, ResourceQuantity>
                                    {
                                        ["cpu"] = new("2"),
                                        ["memory"] = new("2Gi"),
                                    },
                                },
                                SecurityContext = new V1SecurityContext
                                {
                                    RunAsNonRoot = true,
                                    AllowPrivilegeEscalation = false,
                                    Capabilities = new V1Capabilities { Drop = ["ALL"] },
                                },
                            },
                        ],
                        Volumes =
                        [
                            new V1Volume
                            {
                                Name = WorkVolumeName,
                                EmptyDir = new V1EmptyDirVolumeSource { SizeLimit = new ResourceQuantity(options.WorkVolumeSizeLimit) },
                            },
                        ],
                    },
                },
            },
        };
    }

    // DNS-1123 label (<=63 chars): ic-payer-build-{slug}-{revision-suffix}-{rand}.
    public static string BuildJobName(string slug, string revision)
    {
        var slugPart = Truncate(Sanitize(slug), 20);
        var revPart = Truncate(Sanitize(revision), 20);
        var rand = Guid.NewGuid().ToString("n")[..6];
        var name = $"ic-payer-build-{slugPart}-{revPart}-{rand}".Trim('-');
        return Truncate(name, 63).Trim('-');
    }

    private static string Sanitize(string value)
    {
        var chars = value.ToLowerInvariant()
            .Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-')
            .ToArray();
        return new string(chars).Trim('-');
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
