namespace TianShu.Cli.Interaction.Host;

/// <summary>
/// Owns the background refresh timer for the composer dock working status.
/// 持有 Composer Dock 工作状态的后台刷新计时器，避免 Host 直接维护计时生命周期。
/// </summary>
internal sealed class WorkingDockTimerController : IDisposable
{
    private readonly object syncRoot;
    private readonly Func<bool> isHumanOutput;
    private readonly Func<bool> isScriptMode;
    private readonly Action refreshTick;
    private CancellationTokenSource? timerCancellation;

    public WorkingDockTimerController(
        object syncRoot,
        Func<bool> isHumanOutput,
        Func<bool> isScriptMode,
        Action refreshTick)
    {
        this.syncRoot = syncRoot ?? throw new ArgumentNullException(nameof(syncRoot));
        this.isHumanOutput = isHumanOutput ?? throw new ArgumentNullException(nameof(isHumanOutput));
        this.isScriptMode = isScriptMode ?? throw new ArgumentNullException(nameof(isScriptMode));
        this.refreshTick = refreshTick ?? throw new ArgumentNullException(nameof(refreshTick));
    }

    public void Start()
    {
        if (!isHumanOutput() || isScriptMode())
        {
            return;
        }

        lock (syncRoot)
        {
            timerCancellation?.Cancel();
            timerCancellation?.Dispose();
            timerCancellation = new CancellationTokenSource();
            var token = timerCancellation.Token;
            _ = Task.Run(
                () => RunAsync(token),
                CancellationToken.None);
        }

        refreshTick();
    }

    public void Stop()
    {
        lock (syncRoot)
        {
            timerCancellation?.Cancel();
            timerCancellation?.Dispose();
            timerCancellation = null;
        }
    }

    public void Dispose()
        => timerCancellation?.Dispose();

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                refreshTick();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }
}
