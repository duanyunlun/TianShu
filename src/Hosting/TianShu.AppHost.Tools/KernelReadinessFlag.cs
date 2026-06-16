namespace TianShu.AppHost.Tools;

internal readonly record struct KernelReadinessToken(int Value);

internal sealed class KernelReadinessFlag
{
    private readonly object gate = new();
    private readonly HashSet<int> tokens = new();
    private readonly TaskCompletionSource<bool> readyTcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private int nextId;
    private bool ready;

    /// <summary>
    /// Returns true if the flag is ready. If there are no active subscriptions, calling this will
    /// automatically mark the flag as ready.
    /// </summary>
    public bool IsReady()
    {
        lock (gate)
        {
            if (ready)
            {
                return true;
            }

            if (tokens.Count == 0)
            {
                MarkReadyUnsafe();
                return true;
            }

            return false;
        }
    }

    public Task<KernelReadinessToken> SubscribeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (gate)
        {
            if (ready)
            {
                throw new InvalidOperationException("readiness flag already ready");
            }

            while (true)
            {
                var value = Interlocked.Increment(ref nextId);
                if (value == 0)
                {
                    continue;
                }

                if (tokens.Add(value))
                {
                    return Task.FromResult(new KernelReadinessToken(value));
                }
            }
        }
    }

    public Task<bool> MarkReadyAsync(KernelReadinessToken token, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (gate)
        {
            if (ready)
            {
                return Task.FromResult(false);
            }

            if (token.Value == 0)
            {
                return Task.FromResult(false);
            }

            if (!tokens.Remove(token.Value))
            {
                return Task.FromResult(false);
            }

            tokens.Clear();
            MarkReadyUnsafe();
            return Task.FromResult(true);
        }
    }

    public Task WaitReadyAsync(CancellationToken cancellationToken)
    {
        if (IsReady())
        {
            return Task.CompletedTask;
        }

        return readyTcs.Task.WaitAsync(cancellationToken);
    }

    private void MarkReadyUnsafe()
    {
        ready = true;
        _ = readyTcs.TrySetResult(true);
    }
}
