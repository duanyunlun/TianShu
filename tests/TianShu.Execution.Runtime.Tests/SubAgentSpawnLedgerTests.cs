using TianShu.Contracts.Agents;
using TianShu.RuntimeComposition;

namespace TianShu.Execution.Runtime.Tests;

public sealed class SubAgentSpawnLedgerTests
{
    [Fact]
    public void TryAdmitSpawn_ShouldDenyWhenDepthWouldExceedQuota()
    {
        var ledger = new SubAgentSpawnLedger();
        var parent = new SubAgentLineage("run-root", "run-child", "run-root", depth: 1, siblingIndex: 0, "ledger-001");

        var decision = ledger.TryAdmitSpawn(parent, new SubAgentSpawnQuota(1, 8, 32, 0));

        Assert.False(decision.Admitted);
        Assert.Equal("subagent.spawn_depth_exceeded", decision.FailureCode);
        Assert.Null(decision.ChildLineage);
    }

    [Fact]
    public void TryAdmitSpawn_ShouldDenyWhenFanoutWouldExceedQuota()
    {
        var ledger = new SubAgentSpawnLedger();
        var parent = CreateRootLineage();

        var decision = ledger.TryAdmitSpawn(parent, new SubAgentSpawnQuota(1, 0, 32, 0));

        Assert.False(decision.Admitted);
        Assert.Equal("subagent.fanout_exceeded", decision.FailureCode);
    }

    [Fact]
    public void TryAdmitSpawn_ShouldDenyWhenTreeNodeBudgetWouldExceedQuota()
    {
        var ledger = new SubAgentSpawnLedger(initialTreeNodes: 1);
        var parent = CreateRootLineage();

        var decision = ledger.TryAdmitSpawn(parent, new SubAgentSpawnQuota(1, 8, 1, 0));

        Assert.False(decision.Admitted);
        Assert.Equal("subagent.tree_node_budget_exhausted", decision.FailureCode);
    }

    [Fact]
    public void TryAdmitSpawn_ShouldNotUseConcurrencyGateWhenQuotaDisablesIt()
    {
        var ledger = new SubAgentSpawnLedger();
        var parent = CreateRootLineage();
        var quota = new SubAgentSpawnQuota(1, 3, 4, 0);

        var first = ledger.TryAdmitSpawn(parent, quota);
        var second = ledger.TryAdmitSpawn(parent, quota);
        var third = ledger.TryAdmitSpawn(parent, quota);

        Assert.True(first.Admitted);
        Assert.True(second.Admitted);
        Assert.True(third.Admitted);
        Assert.NotEqual("subagent.concurrency_exceeded", first.FailureCode);
        Assert.NotEqual("subagent.concurrency_exceeded", second.FailureCode);
        Assert.NotEqual("subagent.concurrency_exceeded", third.FailureCode);
    }

    [Fact]
    public void OnChildTerminated_ShouldNotReclaimMonotonicFanout()
    {
        var ledger = new SubAgentSpawnLedger();
        var parent = CreateRootLineage();
        var quota = new SubAgentSpawnQuota(1, 1, 32, 1);

        var first = ledger.TryAdmitSpawn(parent, quota);
        Assert.True(first.Admitted);
        ledger.OnChildTerminated(first.ChildLineage!.CurrentRunId);

        var second = ledger.TryAdmitSpawn(parent, quota);

        Assert.False(second.Admitted);
        Assert.Equal("subagent.fanout_exceeded", second.FailureCode);
    }

    [Fact]
    public void OnChildTerminated_ShouldNotReclaimMonotonicTreeNodes()
    {
        var ledger = new SubAgentSpawnLedger(initialTreeNodes: 1);
        var parent = CreateRootLineage();
        var quota = new SubAgentSpawnQuota(1, 8, 2, 1);

        var first = ledger.TryAdmitSpawn(parent, quota);
        Assert.True(first.Admitted);
        ledger.OnChildTerminated(first.ChildLineage!.CurrentRunId);

        var second = ledger.TryAdmitSpawn(parent, quota);

        Assert.False(second.Admitted);
        Assert.Equal("subagent.tree_node_budget_exhausted", second.FailureCode);
    }

    [Fact]
    public void TryAdmitSpawn_ShouldEnforceAndReclaimConcurrentAgentsWhenEnabled()
    {
        var ledger = new SubAgentSpawnLedger();
        var parent = CreateRootLineage();
        var quota = new SubAgentSpawnQuota(1, 8, 32, 1);

        var first = ledger.TryAdmitSpawn(parent, quota);
        var second = ledger.TryAdmitSpawn(parent, quota);

        Assert.True(first.Admitted);
        Assert.False(second.Admitted);
        Assert.Equal("subagent.concurrency_exceeded", second.FailureCode);

        ledger.OnChildTerminated(first.ChildLineage!.CurrentRunId);
        var third = ledger.TryAdmitSpawn(parent, quota);

        Assert.True(third.Admitted);
    }

    [Fact]
    public void TryAdmitSpawn_ShouldDeriveDeterministicChildRunIdFromParentAndSiblingIndex()
    {
        var firstLedger = new SubAgentSpawnLedger();
        var secondLedger = new SubAgentSpawnLedger();
        var parent = CreateRootLineage();
        var quota = new SubAgentSpawnQuota(1, 8, 32, 0);

        var first = firstLedger.TryAdmitSpawn(parent, quota);
        var second = secondLedger.TryAdmitSpawn(parent, quota);
        var nextSibling = firstLedger.TryAdmitSpawn(parent, quota);

        Assert.True(first.Admitted);
        Assert.True(second.Admitted);
        Assert.True(nextSibling.Admitted);
        Assert.Equal(first.ChildLineage!.CurrentRunId, second.ChildLineage!.CurrentRunId);
        Assert.NotEqual(first.ChildLineage.CurrentRunId, nextSibling.ChildLineage!.CurrentRunId);
        Assert.Equal(0, first.ChildLineage.SiblingIndex);
        Assert.Equal(1, nextSibling.ChildLineage.SiblingIndex);
    }

    private static SubAgentLineage CreateRootLineage()
        => new("run-root", "run-root", null, depth: 0, siblingIndex: 0, "ledger-001");
}
