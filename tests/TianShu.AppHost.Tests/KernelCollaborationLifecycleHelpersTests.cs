using System.Text.Json;
using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;

namespace TianShu.AppHost.Tests;

public sealed class KernelCollaborationLifecycleHelpersTests
{
    [Fact]
    public void TryCreateCollabLifecycleDescriptor_WhenSpawnAgentRequestValid_ShouldBuildPromptAndModel()
    {
        using var arguments = JsonDocument.Parse(
            """
            {
              "message": "继续排查",
              "agent_type": "explorer",
              "fork_context": false,
              "model": "gpt-5.2",
              "reasoning_effort": "high"
            }
            """);

        var created = KernelCollaborationLifecycleHelpers.TryCreateCollabLifecycleDescriptor(
            "spawn_agent",
            arguments.RootElement,
            out var descriptor);

        Assert.True(created);
        Assert.Equal("spawnAgent", descriptor.Tool);
        Assert.Equal("继续排查", descriptor.Prompt);
        Assert.Equal("gpt-5.2", descriptor.Model);
        Assert.Equal("high", descriptor.ReasoningEffort);
        Assert.Empty(descriptor.ReceiverThreadIds);
    }

    [Fact]
    public void BuildCollabCompletedState_WhenWaitContainsErroredStatus_ShouldReturnFailedState()
    {
        using var arguments = JsonDocument.Parse("""{ "ids": ["agent_a", "agent_b"] }""");
        var descriptor = new KernelCollabLifecycleDescriptor("wait", ["agent_a", "agent_b"], null, null, null);
        var result = new KernelToolResult(
            success: true,
            outputText:
            """
            {
              "status": {
                "agent_a": { "completed": "done" },
                "agent_b": { "errored": "boom" }
              }
            }
            """);

        var completed = KernelCollaborationLifecycleHelpers.BuildCollabCompletedState(
            "wait",
            arguments.RootElement,
            result,
            descriptor);

        Assert.Equal("failed", completed.Status);
        Assert.Equal(["agent_a", "agent_b"], completed.ReceiverThreadIds);

        var completedState = JsonSerializer.SerializeToElement(completed.AgentsStates["agent_a"]);
        var erroredState = JsonSerializer.SerializeToElement(completed.AgentsStates["agent_b"]);
        Assert.Equal("completed", completedState.GetProperty("status").GetString());
        Assert.Equal("done", completedState.GetProperty("message").GetString());
        Assert.Equal("errored", erroredState.GetProperty("status").GetString());
        Assert.Equal("boom", erroredState.GetProperty("message").GetString());
    }

    [Fact]
    public void BuildCollabCompletedState_WhenSendInputSucceeds_ShouldMarkReceiverRunning()
    {
        using var arguments = JsonDocument.Parse("""{ "id": "agent_001" }""");
        var descriptor = new KernelCollabLifecycleDescriptor("sendInput", ["agent_001"], "继续执行", null, null);
        var result = new KernelToolResult(success: true, outputText: """{ "submission_id": "sub_001" }""");

        var completed = KernelCollaborationLifecycleHelpers.BuildCollabCompletedState(
            "send_input",
            arguments.RootElement,
            result,
            descriptor);

        Assert.Equal("completed", completed.Status);
        Assert.Equal(["agent_001"], completed.ReceiverThreadIds);

        var state = JsonSerializer.SerializeToElement(completed.AgentsStates["agent_001"]);
        Assert.Equal("running", state.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, state.GetProperty("message").ValueKind);
    }

    [Fact]
    public void CreateCollabToolCallItem_ShouldProduceExpectedPayloadShape()
    {
        var item = KernelCollaborationLifecycleHelpers.CreateCollabToolCallItem(
            itemId: "item_001",
            tool: "spawnAgent",
            status: "completed",
            senderThreadId: "thread_parent",
            receiverThreadIds: ["thread_child"],
            prompt: "继续分析",
            model: "gpt-5.2",
            reasoningEffort: "high",
            agentsStates: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["thread_child"] = new { status = "running", message = (string?)null },
            });
        var payload = JsonSerializer.SerializeToElement(item);

        Assert.Equal("item_001", payload.GetProperty("id").GetString());
        Assert.Equal("collabAgentToolCall", payload.GetProperty("type").GetString());
        Assert.Equal("spawnAgent", payload.GetProperty("tool").GetString());
        Assert.Equal("completed", payload.GetProperty("status").GetString());
        Assert.Equal("thread_parent", payload.GetProperty("senderThreadId").GetString());
        Assert.Equal("thread_child", payload.GetProperty("receiverThreadIds")[0].GetString());
        Assert.Equal("继续分析", payload.GetProperty("prompt").GetString());
        Assert.Equal("gpt-5.2", payload.GetProperty("model").GetString());
        Assert.Equal("high", payload.GetProperty("reasoningEffort").GetString());
    }
}
