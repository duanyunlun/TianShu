using TianShu.Cli.Interaction.Orchestration;

namespace TianShu.Cli.Tests;

public sealed class PendingInteractiveRequestStoreTests
{
    [Fact]
    public void Snapshot_ReturnsSortedCallIdsAndCounts()
    {
        var store = new PendingInteractiveRequestStore();

        store.AddApproval(new CliPendingApprovalRequestState("approval_b", "thread_1", "turn_1", "shell", null, null, null));
        store.AddApproval(new CliPendingApprovalRequestState("approval_a", "thread_1", "turn_1", "edit", null, null, null));
        store.AddPermission(new CliPendingPermissionRequestState("permission_a", "thread_1", "turn_1", "shell"));
        store.AddUserInput(new CliPendingUserInputRequestState("input_a", "thread_1", "turn_1", null));

        var snapshot = store.Snapshot;

        Assert.Equal(["approval_a", "approval_b"], snapshot.ApprovalCallIds);
        Assert.Equal(["permission_a"], snapshot.PermissionCallIds);
        Assert.Equal(["input_a"], snapshot.UserInputCallIds);
        Assert.Equal(2, snapshot.ApprovalCount);
        Assert.Equal(1, snapshot.PermissionCount);
        Assert.Equal(1, snapshot.UserInputCount);
        Assert.False(store.IsEmpty);
    }

    [Fact]
    public void Remove_RemovesMatchingCallIdAcrossRequestKinds()
    {
        var store = new PendingInteractiveRequestStore();
        store.AddApproval(new CliPendingApprovalRequestState("same", "thread_1", null, "shell", null, null, null));
        store.AddPermission(new CliPendingPermissionRequestState("same", "thread_1", null, "shell"));
        store.AddUserInput(new CliPendingUserInputRequestState("same", "thread_1", null, null));

        store.Remove("same");

        Assert.True(store.IsEmpty);
    }

    [Fact]
    public void ClearForTurn_RemovesRequestsMatchingThreadAndTurn()
    {
        var store = new PendingInteractiveRequestStore();
        store.AddApproval(new CliPendingApprovalRequestState("approval_keep_thread", "thread_other", "turn_1", null, null, null, null));
        store.AddPermission(new CliPendingPermissionRequestState("permission_remove_turn", "thread_1", "turn_1", null));
        store.AddUserInput(new CliPendingUserInputRequestState("input_remove_missing_turn", "thread_1", null, null));
        store.AddApproval(new CliPendingApprovalRequestState("approval_keep_turn", "thread_1", "turn_2", null, null, null, null));

        store.ClearForTurn("thread_1", "turn_1");

        var snapshot = store.Snapshot;
        Assert.Equal(["approval_keep_thread", "approval_keep_turn"], snapshot.ApprovalCallIds);
        Assert.Empty(snapshot.PermissionCallIds);
        Assert.Empty(snapshot.UserInputCallIds);
    }

    [Fact]
    public void Clear_RemovesAllRequests()
    {
        var store = new PendingInteractiveRequestStore();
        store.AddApproval(new CliPendingApprovalRequestState("approval", "thread_1", null, null, null, null, null));
        store.AddPermission(new CliPendingPermissionRequestState("permission", "thread_1", null, null));
        store.AddUserInput(new CliPendingUserInputRequestState("input", "thread_1", null, null));

        store.Clear();

        Assert.True(store.IsEmpty);
        Assert.Equal(0, store.Snapshot.ApprovalCount);
        Assert.Equal(0, store.Snapshot.PermissionCount);
        Assert.Equal(0, store.Snapshot.UserInputCount);
    }

    [Fact]
    public void TryGetApproval_ReturnsPendingApprovalForResolver()
    {
        var store = new PendingInteractiveRequestStore();
        var approval = new CliPendingApprovalRequestState("approval", "thread_1", "turn_1", "shell", "exec", ["allow"], null);
        store.AddApproval(approval);

        Assert.True(store.TryGetApproval("approval", out var actual));
        Assert.Equal(approval, actual);
    }
}
