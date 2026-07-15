using System.Collections.Concurrent;
using Pronto.BillerExperience.Api.Domain;
using Pronto.BillerExperience.Contracts.V1.Onboarding;

namespace Pronto.BillerExperience.Api.Infrastructure.Persistence;

public sealed class InMemoryBillerExperienceRepository : IBillerExperienceRepository
{
    private readonly ConcurrentDictionary<string, BillerRecord> _billers = new();
    private readonly ConcurrentDictionary<string, string> _slugReservations = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ExperienceRecord> _experiences = new();
    private readonly ConcurrentDictionary<string, OnboardingRunRecord> _runs = new();
    private readonly ConcurrentDictionary<string, AgentActivityEvent> _agentActivity = new();
    private readonly ConcurrentDictionary<string, AgentContextRecord> _agentContexts = new();
    private readonly ConcurrentDictionary<string, DeploymentRecord> _deployments = new();
    private readonly object _writeSync = new();

    public ValueTask<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_slugReservations.ContainsKey(slug));
    }

    public ValueTask<BillerRecord> CreateBillerAsync(BillerRecord biller, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Atomic slug reservation closes the check-then-create race: two concurrent creations
        // racing on the same slug cannot both win, because TryAdd is atomic.
        if (!_slugReservations.TryAdd(biller.Slug, biller.Id))
        {
            throw new SlugConflictException(biller.Slug);
        }

        if (!_billers.TryAdd(biller.Id, biller))
        {
            _slugReservations.TryRemove(biller.Slug, out _);
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
        lock (_writeSync)
        {
            if (_experiences.TryGetValue(key, out var current) && expectedETag is not null && current.ETag != expectedETag)
            {
                throw new ConcurrencyException("The experience was modified by another request.");
            }

            var saved = experience with { ETag = Guid.NewGuid().ToString("N") };
            _experiences[key] = saved;
            return ValueTask.FromResult(saved);
        }
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
        lock (_writeSync)
        {
            if (_runs.TryGetValue(key, out var current) && expectedETag is not null && current.ETag != expectedETag)
            {
                throw new ConcurrencyException("The onboarding run was modified by another request.");
            }

            var saved = run with { ETag = Guid.NewGuid().ToString("N") };
            _runs[key] = saved;
            return ValueTask.FromResult(saved);
        }
    }

    public ValueTask AppendAgentActivityAsync(
        string billerId,
        string runId,
        AgentActivityEvent activity,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _agentActivity[$"{billerId}:{runId}:{activity.EventId}"] = activity;
        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<AgentActivityEvent>> GetAgentActivityAsync(
        string billerId,
        string runId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<AgentActivityEvent> result = _agentActivity
            .Where(item => item.Key.StartsWith($"{billerId}:{runId}:", StringComparison.Ordinal))
            .Select(item => item.Value)
            .OrderBy(item => item.Sequence)
            .ThenBy(item => item.OccurredAt)
            .ToArray();
        return ValueTask.FromResult(result);
    }

    public ValueTask<AgentContextRecord?> GetAgentContextAsync(
        string billerId,
        string runId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _agentContexts.TryGetValue($"{billerId}:{runId}", out var context);
        return ValueTask.FromResult(context);
    }

    public ValueTask<AgentContextRecord> SaveAgentContextAsync(
        AgentContextRecord context,
        string? expectedETag,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = $"{context.BillerId}:{context.RunId}";
        lock (_writeSync)
        {
            if (_agentContexts.TryGetValue(key, out var current) &&
                expectedETag is not null &&
                current.ETag != expectedETag)
            {
                throw new ConcurrencyException("The shared agent context was modified by another request.");
            }

            var saved = context with { ETag = Guid.NewGuid().ToString("N") };
            _agentContexts[key] = saved;
            return ValueTask.FromResult(saved);
        }
    }

    public ValueTask<DeploymentRecord> CreateDeploymentAsync(DeploymentRecord deployment, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var saved = deployment with { ETag = Guid.NewGuid().ToString("N") };
        if (!_deployments.TryAdd($"{deployment.BillerId}:{deployment.Id}", saved))
        {
            throw new ConcurrencyException("This revision already has a publication request.");
        }

        return ValueTask.FromResult(saved);
    }

    public ValueTask<DeploymentRecord> SaveDeploymentAsync(
        DeploymentRecord deployment,
        string? expectedETag,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = $"{deployment.BillerId}:{deployment.Id}";
        if (!_deployments.TryGetValue(key, out var current) ||
            (expectedETag is not null && current.ETag != expectedETag))
        {
            throw new ConcurrencyException("This publication request was modified by another request.");
        }

        var saved = deployment with { ETag = Guid.NewGuid().ToString("N") };
        if (!_deployments.TryUpdate(key, saved, current))
        {
            throw new ConcurrencyException("This publication request was modified by another request.");
        }

        return ValueTask.FromResult(saved);
    }

    public ValueTask<DeploymentRecord?> GetDeploymentAsync(string billerId, string deploymentId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _deployments.TryGetValue($"{billerId}:{deploymentId}", out var deployment);
        return ValueTask.FromResult(deployment);
    }

    public ValueTask PurgeByBillerAsync(string billerId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _billers.TryRemove(billerId, out _);
        RemoveWhere(_slugReservations, reservedBillerId => reservedBillerId == billerId);
        RemoveWhere(_experiences, item => item.BillerId == billerId);
        RemoveWhere(_runs, item => item.BillerId == billerId);
        RemoveWhere(_deployments, item => item.BillerId == billerId);
        RemoveByKey(_agentActivity, key => key.StartsWith($"{billerId}:", StringComparison.Ordinal));
        RemoveWhere(_agentContexts, item => item.BillerId == billerId);
        return ValueTask.CompletedTask;
    }

    private static void RemoveWhere<T>(ConcurrentDictionary<string, T> map, Func<T, bool> predicate)
    {
        foreach (var pair in map.Where(pair => predicate(pair.Value)).ToList())
        {
            map.TryRemove(pair.Key, out _);
        }
    }

    private static void RemoveByKey<T>(ConcurrentDictionary<string, T> map, Func<string, bool> predicate)
    {
        foreach (var pair in map.Where(pair => predicate(pair.Key)).ToList())
        {
            map.TryRemove(pair.Key, out _);
        }
    }
}
