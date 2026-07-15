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
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxConcurrency, 1);
        using var gate = new SemaphoreSlim(maxConcurrency);
        var tasks = inputs.Select(async input =>
        {
            await gate.WaitAsync(cancellationToken);
            var attempts = 0;
            try
            {
                var output = await ResilientExecution.ExecuteAsync(
                    async (attempt, token) => { attempts = attempt; return await operation(input, attempt, token); },
                    policy ?? new OrchestrationExecutionPolicy(MaxAttempts: 1),
                    cancellationToken: cancellationToken);
                return new FanOutResult<TInput, TOutput>(input, output, null, attempts);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                return new FanOutResult<TInput, TOutput>(input, default, exception, Math.Max(attempts, 1));
            }
            finally { gate.Release(); }
        });
        return await Task.WhenAll(tasks);
    }
}
