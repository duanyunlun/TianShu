using TianShu.Contracts.Kernel;

namespace TianShu.Kernel;

/// <summary>
/// Kernel 运行状态机守护，禁止跳过 Stable Kernel Core 的验证阶段。
/// Kernel run-state guard that prevents skipping Stable Kernel Core validation phases.
/// </summary>
public static class KernelRunStateMachine
{
    public static bool CanTransition(KernelRunLifecycleState current, KernelRunLifecycleState target)
        => (current, target) switch
        {
            (KernelRunLifecycleState.Created, KernelRunLifecycleState.IntentAccepted) => true,
            (KernelRunLifecycleState.IntentAccepted, KernelRunLifecycleState.GraphSelected) => true,
            (KernelRunLifecycleState.GraphSelected, KernelRunLifecycleState.ProposalPending) => true,
            (KernelRunLifecycleState.ProposalPending, KernelRunLifecycleState.GraphValidated) => true,
            (KernelRunLifecycleState.GraphValidated, KernelRunLifecycleState.Executing) => true,
            (KernelRunLifecycleState.Executing, KernelRunLifecycleState.Paused) => true,
            (KernelRunLifecycleState.Executing, KernelRunLifecycleState.Completed) => true,
            (KernelRunLifecycleState.Executing, KernelRunLifecycleState.Failed) => true,
            (KernelRunLifecycleState.Paused, KernelRunLifecycleState.Executing) => true,
            (KernelRunLifecycleState.Paused, KernelRunLifecycleState.Recovering) => true,
            (KernelRunLifecycleState.Recovering, KernelRunLifecycleState.GraphSelected) => true,
            (KernelRunLifecycleState.Recovering, KernelRunLifecycleState.Failed) => true,
            (KernelRunLifecycleState.Recovering, KernelRunLifecycleState.RolledBack) => true,
            _ => false,
        };
}
