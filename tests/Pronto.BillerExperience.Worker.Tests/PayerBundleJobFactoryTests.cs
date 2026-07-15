using System.Text;
using Pronto.BillerExperience.Worker.Building;
using Xunit;

namespace Pronto.BillerExperience.Worker.Tests;

public sealed class PayerBundleJobFactoryTests
{
    private static readonly BundleBuildOptions Options = new()
    {
        BuilderImage = "acr.example.test/ic-payer-experience-builder:latest",
        Namespace = "ic",
        ServiceAccountName = "ic-workload",
        GeneratorMode = "deterministic",
    };

    private static BundleBuildRequest Request(string slug = "acme", string revision = "config-3") =>
        new("biller-1", slug, revision, "{\"biller_id\":\"acme\"}", "https://blob.example.test/", "payer-experiences");

    [Fact]
    public void JobRunsBuilderImageWithWorkloadIdentityAndServiceAccount()
    {
        var job = PayerBundleJobFactory.Create(Request(), Options);
        var pod = job.Spec.Template.Spec;

        Assert.Equal("ic-workload", pod.ServiceAccountName);
        Assert.Equal("true", job.Spec.Template.Metadata.Labels["azure.workload.identity/use"]);
        Assert.Equal("Never", pod.RestartPolicy);
        Assert.Equal(0, job.Spec.BackoffLimit);
        Assert.Equal(Options.BuilderImage, Assert.Single(pod.Containers).Image);
    }

    [Fact]
    public void JobPassesBuildInputsAsEnvIncludingSkipActiveAndBase64Definition()
    {
        var request = Request();
        var job = PayerBundleJobFactory.Create(request, Options);
        var env = Assert.Single(job.Spec.Template.Spec.Containers)
            .Env.ToDictionary(e => e.Name, e => e.Value);

        Assert.Equal("acme", env["PAYER_SLUG"]);
        Assert.Equal("config-3", env["PAYER_REVISION"]);
        Assert.Equal("deterministic", env["PAYER_MODE"]);
        Assert.Equal("https://blob.example.test/", env["PAYER_STORAGE_ENDPOINT"]);
        Assert.Equal("payer-experiences", env["PAYER_CONTAINER"]);
        Assert.Equal("true", env["PAYER_PUBLISH"]);
        // The Worker's config publisher owns the atomic active.json flip.
        Assert.Equal("true", env["PAYER_SKIP_ACTIVE"]);

        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(env["PAYER_DEFINITION_B64"]));
        Assert.Equal(request.DefinitionJson, decoded);
    }

    [Fact]
    public void ContainerRunsNonRootWithDroppedCapabilities()
    {
        var job = PayerBundleJobFactory.Create(Request(), Options);
        var container = Assert.Single(job.Spec.Template.Spec.Containers);

        Assert.True(container.SecurityContext.RunAsNonRoot);
        Assert.False(container.SecurityContext.AllowPrivilegeEscalation);
        Assert.Contains("ALL", container.SecurityContext.Capabilities.Drop);
        Assert.True(job.Spec.Template.Spec.SecurityContext.RunAsNonRoot);
    }

    [Theory]
    [InlineData("ACME Corp!!", "config-3")]
    [InlineData("a-very-long-slug-name-that-exceeds-limits", "rev-1784079859953")]
    public void JobNameIsDnsSafeAndBounded(string slug, string revision)
    {
        var name = PayerBundleJobFactory.BuildJobName(slug, revision);

        Assert.True(name.Length <= 63);
        Assert.Matches("^[a-z0-9]([-a-z0-9]*[a-z0-9])?$", name);
        Assert.StartsWith("ic-payer-build-", name);
    }
}
