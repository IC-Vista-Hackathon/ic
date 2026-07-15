namespace Pronto.Agentic.Orchestration.Execution;

public sealed record OrchestrationExecutionPolicy(
    int MaxAttempts = 3,
    TimeSpan? AttemptTimeout = null,
    TimeSpan? TotalBudget = null,
    TimeSpan? InitialBackoff = null,
    TimeSpan? MaxBackoff = null,
    Func<Exception, bool>? IsRetryable = null)
{
    internal bool ShouldRetry(Exception exception) =>
        (IsRetryable?.Invoke(exception) ?? exception is TimeoutException or HttpRequestException) &&
        exception is not OperationCanceledException;
}

public static class ResilientExecution
{
    public static async ValueTask<T> ExecuteAsync<T>(
        Func<int, CancellationToken, ValueTask<T>> operation,
        OrchestrationExecutionPolicy policy,
        Func<int, Exception, TimeSpan, CancellationToken, ValueTask>? onRetry = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentOutOfRangeException.ThrowIfLessThan(policy.MaxAttempts, 1);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (policy.TotalBudget is { } totalBudget) budget.CancelAfter(totalBudget);

        for (var attempt = 1; ; attempt++)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(budget.Token);
            if (policy.AttemptTimeout is { } attemptTimeout) timeout.CancelAfter(attemptTimeout);
            try
            {
                return await operation(attempt, timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested &&
                                                      !budget.IsCancellationRequested &&
                                                      attempt < policy.MaxAttempts)
            {
                var exception = new TimeoutException($"Orchestration attempt {attempt} timed out.");
                await DelayAsync(attempt, exception);
            }
            catch (Exception exception) when (attempt < policy.MaxAttempts && policy.ShouldRetry(exception))
            {
                await DelayAsync(attempt, exception);
            }
        }

        async ValueTask DelayAsync(int attempt, Exception exception)
        {
            var initial = policy.InitialBackoff ?? TimeSpan.FromMilliseconds(100);
            var maximum = policy.MaxBackoff ?? TimeSpan.FromSeconds(2);
            var delay = TimeSpan.FromMilliseconds(Math.Min(maximum.TotalMilliseconds,
                initial.TotalMilliseconds * Math.Pow(2, attempt - 1)));
            if (onRetry is not null) await onRetry(attempt, exception, delay, budget.Token);
            await Task.Delay(delay, budget.Token);
        }
    }
}
