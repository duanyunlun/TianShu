using TianShu.Cli.Interaction;
using TianShu.Cli.Interaction.Commands;
using TianShu.Cli.Interaction.Rendering;
using TianShu.Contracts.Conversations;
using TianShu.Contracts.Primitives;

namespace TianShu.Cli.Tests;

public sealed class InteractionPipelineTests
{
    [Fact]
    public async Task ExecuteInputLineAsync_UsesCommandLoopAsInputEntryPoint()
    {
        var calls = new List<string>();
        var pipeline = new InteractionPipeline(new ChatCommandExecutionContext(
            input => calls.Add("record:" + input),
            input => calls.Add("user:" + input),
            (input, _) =>
            {
                calls.Add("slash:" + input);
                return Task.FromResult(true);
            },
            (_, _) => Task.FromResult(false),
            (_, _) => Task.FromResult(false),
            (_, _, _) => Task.FromResult(false),
            (_, _) => Task.FromResult(false),
            (_, _) => Task.CompletedTask));

        var shouldExit = await pipeline.ExecuteInputLineAsync(" /exit ", CancellationToken.None);

        Assert.True(shouldExit);
        Assert.Equal(["record:/exit", "slash:/exit"], calls);
    }

    [Fact]
    public void ProjectStreamEvent_CommitsAssistantBeforeTool_AndExposesProjectionState()
    {
        var pipeline = new InteractionPipeline();
        pipeline.ProjectStreamEvent(AssistantDelta("先确认目录状态。"));

        var result = pipeline.ProjectStreamEvent(ToolStarted(
            callId: "call-shell-1",
            inputJson: "{\"command\":\"Get-Location\"}"));

        var assistantBlock = Assert.IsType<AssistantMessageBlock>(Assert.Single(result.Projection.CommittedBlocks));
        Assert.Equal("先确认目录状态。", assistantBlock.Text);
        Assert.False(result.State.HasActiveAssistantText);
        Assert.Contains(result.Projection.Records, record => record.Kind == "assistant_block_committed" && record.Reason == "before_tool");
        Assert.Contains(result.Projection.Records, record => record.Kind == "tool_input_cached");
    }

    [Fact]
    public void CaptureIncompleteAssistantSnapshot_ReturnsRecordForActiveAssistantText()
    {
        var pipeline = new InteractionPipeline();
        pipeline.ProjectStreamEvent(AssistantDelta("仍在输出。"));

        var record = pipeline.CaptureIncompleteAssistantSnapshot(DateTimeOffset.Parse("2026-05-12T00:00:00Z"), "timeout");

        Assert.NotNull(record);
        Assert.Equal("assistant_block_incomplete", record!.Kind);
        Assert.Equal("仍在输出。", record.Text);
        Assert.Equal("assistant", record.BlockType);
        Assert.Equal("timeout", record.Reason);
    }

    private static ControlPlaneConversationStreamEvent AssistantDelta(string text)
        => new()
        {
            Kind = ControlPlaneConversationStreamEventKind.AssistantTextDelta,
            ThreadId = new ThreadId("thread-1"),
            TurnId = new TurnId("turn-1"),
            Text = text,
        };

    private static ControlPlaneConversationStreamEvent ToolStarted(string callId, string inputJson)
        => new()
        {
            Kind = ControlPlaneConversationStreamEventKind.ToolCallStarted,
            ThreadId = new ThreadId("thread-1"),
            TurnId = new TurnId("turn-1"),
            CallId = new CallId(callId),
            ToolName = "shell",
            PayloadKind = ControlPlaneConversationStreamPayloadKind.ToolCall,
            Payload = StructuredValueTestHelper.FromJson(
                $$"""
                {
                  "toolName": "shell",
                  "callId": "{{callId}}",
                  "inputText": {{System.Text.Json.JsonSerializer.Serialize(inputJson)}},
                  "status": "in_progress"
                }
                """),
        };
}
