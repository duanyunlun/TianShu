using System.Text.Json;
using TianShu.Cli.Interaction.Commands.State;
using TianShu.Cli.Interaction.Orchestration;
using TianShu.Contracts.Sessions;

namespace TianShu.Cli.Tests;

public sealed class InteractiveStateCommandHandlerTests
{
    [Fact]
    public async Task HandleStateCommandAsync_WritesDiagnosticJsonSnapshot()
    {
        var runtime = new CliConsumerFakeRuntime
        {
            ActiveThreadId = "thread_state_1",
            HasActiveTurn = true,
        };
        var pendingRequests = new PendingInteractiveRequestStore();
        pendingRequests.AddApproval(new CliPendingApprovalRequestState("approval_1", "thread_state_1", "turn_1", "shell", null, null, null));
        var handler = new InteractiveStateCommandHandler();
        var output = new List<(string Text, bool IsError)>();
        ControlPlaneSessionSnapshot? appliedSnapshot = null;
        var context = new InteractiveStateCommandContext(
            new ChatCommandOptions { ApproveAll = true },
            pendingRequests,
            new RestoredFollowUpCoordinator(),
            null,
            null,
            new JsonSerializerOptions(JsonSerializerDefaults.Web),
            snapshot => appliedSnapshot = snapshot,
            (text, isError) => output.Add((text, isError)));

        await handler.HandleStateCommandAsync(runtime, context, CancellationToken.None);

        Assert.NotNull(appliedSnapshot);
        var line = Assert.Single(output);
        Assert.False(line.IsError);
        using var document = JsonDocument.Parse(line.Text);
        Assert.Equal("thread_state_1", document.RootElement.GetProperty("threadId").GetString());
        Assert.True(document.RootElement.GetProperty("hasActiveTurn").GetBoolean());
        Assert.Equal("approval_1", document.RootElement.GetProperty("pendingApprovalCallIds")[0].GetString());
        Assert.True(document.RootElement.GetProperty("approveAll").GetBoolean());
    }
}
