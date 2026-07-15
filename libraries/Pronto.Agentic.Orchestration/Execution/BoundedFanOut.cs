namespace Pronto.Agentic.Orchestration.Execution;

public sealed record FanOutResult<TInput, TOutput>(
    TInput Input,
    TOutput? Output,
    Exception? Error,
    int Attempts)
{
    public bool Succeeded => Error is null;
}

public static class BoundedFanOut
{
    public static async ValueTask<IReadOnlyList<FanOutResult<TInput, TOutput>>> ExecuteAsync<TInput, TOutput>(
        IReadOnlyCollection<TInput> inputs,
        int maxConcurrency,
        Func<TInput, int, CancellationToken, ValueTask<TOutput>> operation,
        OrchestrationExecutionPolicy? policy = null,
        Func<TInput, int, Exception, TimeSpan, CancellationToken, ValueTask>? onRetry = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxConcurrency, 1);
        using var gate = new SemaphoreSlim(maxConcurrency);
        var tasks = inputs.Select(async input =>
        {
            var attempts = 0;
            try
            {
                var output = await ResilientExecution.ExecuteAsync(
                    // The concurrency gate is acquired per attempt so it is not held across
                    // retry backoff delays, which would otherwise collapse effective parallelism.
                    async (attempt, token) =>
                    {
                        attempts = attempt;
                        await gate.WaitAsync(token);
                        try { return await operation(input, attempt, token); }
                        finally { gate.Release(); }
                    },
                    policy ?? new OrchestrationExecutionPolicy(MaxAttempts: 1),
                    onRetry: onRetry is null
                        ? null
                        : (attempt, exception, delay, token) => onRetry(input, attempt, exception, delay, token),
                    cancellationToken: cancellationToken);
                return new FanOutResult<TInput, TOutput>(input, output, null, attempts);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                return new FanOutResult<TInput, TOutput>(input, default, exception, Math.Max(attempts, 1));
            }
        });
        return await Task.WhenAll(tasks);
    }
}
