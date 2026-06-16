namespace TianShu.AppHost.State;

internal sealed record KernelSpawnAgentGuardConfiguration(int MaxThreads, int MaxDepth);

internal sealed class KernelSpawnSlotReservation : IDisposable
{
    private readonly KernelSpawnAgentGuardState state;
    private bool active = true;

    public KernelSpawnSlotReservation(KernelSpawnAgentGuardState state)
    {
        this.state = state;
    }

    public void Commit(string threadId)
    {
        if (!active)
        {
            return;
        }

        state.Commit(threadId);
        active = false;
    }

    public void Dispose()
    {
        if (!active)
        {
            return;
        }

        state.ReleaseReserved();
        active = false;
    }
}
