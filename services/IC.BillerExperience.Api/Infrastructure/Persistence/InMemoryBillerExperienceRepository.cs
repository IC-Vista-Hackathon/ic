using System.Collections.Concurrent;
using IC.BillerExperience.Api.Domain;

namespace IC.BillerExperience.Api.Infrastructure.Persistence;

public sealed class InMemoryBillerExperienceRepository : IBillerExperienceRepository
{
    private readonly ConcurrentDictionary<string, BillerRecord> _billers = new();
    private readonly ConcurrentDictionary<string, ExperienceRecord> _experiences = new();
    private readonly ConcurrentDictionary<string, OnboardingRunRecord> _runs = new();
    private readonly ConcurrentDictionary<string, DeploymentRecord> _deployments = new();

    public ValueTask<BillerRecord> CreateBillerAsync(BillerRecord biller, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_billers.TryAdd(biller.Id, biller))
        {
            throw new ConcurrencyException($"Biller '{biller.Id}' already exists.");
        }

        return ValueTask.FromResult(biller);
    }

    public ValueTask<BillerRecord?> GetBillerAsync(string billerId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _billers.TryGetValue(billerId, out var biller);
        return ValueTask.FromResult(biller);
    }

    public ValueTask<BillerRecord> SaveBillerAsync(BillerRecord biller, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _billers[biller.Id] = biller;
        return ValueTask.FromResult(biller);
    }

    public ValueTask<ExperienceRecord?> GetLatestExperienceAsync(string billerId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var experience = _experiences.Values
            .Where(item => item.BillerId == billerId)
            .OrderByDescending(item => item.Version)
            .FirstOrDefault();
        return ValueTask.FromResult(experience);
    }

    public ValueTask<ExperienceRecord> SaveExperienceAsync(ExperienceRecord experience, string? expectedETag, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = $"{experience.BillerId}:{experience.Id}";
        if (_experiences.TryGetValue(key, out var current) && expectedETag is not null && current.ETag != expectedETag)
        {
            throw new ConcurrencyException("The experience was modified by another request.");
        }

        var saved = experience with { ETag = Guid.NewGuid().ToString("N") };
        _experiences[key] = saved;
        return ValueTask.FromResult(saved);
    }

    public ValueTask<OnboardingRunRecord?> GetRunAsync(string billerId, string runId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _runs.TryGetValue($"{billerId}:{runId}", out var run);
        return ValueTask.FromResult(run);
    }

    public ValueTask<OnboardingRunRecord> SaveRunAsync(OnboardingRunRecord run, string? expectedETag, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = $"{run.BillerId}:{run.Id}";
        if (_runs.TryGetValue(key, out var current) && expectedETag is not null && current.ETag != expectedETag)
        {
            throw new ConcurrencyException("The onboarding run was modified by another request.");
        }

        var saved = run with { ETag = Guid.NewGuid().ToString("N") };
        _runs[key] = saved;
        return ValueTask.FromResult(saved);
    }

    public ValueTask<DeploymentRecord> CreateDeploymentAsync(DeploymentRecord deployment, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_deployments.TryAdd($"{deployment.BillerId}:{deployment.Id}", deployment))
        {
            throw new ConcurrencyException("This revision already has a publication request.");
        }

        return ValueTask.FromResult(deployment);
    }

    public ValueTask<DeploymentRecord?> GetDeploymentAsync(string billerId, string deploymentId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _deployments.TryGetValue($"{billerId}:{deploymentId}", out var deployment);
        return ValueTask.FromResult(deployment);
    }
}
