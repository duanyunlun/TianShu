using TianShu.Cli.Interaction;
using TianShu.Cli.Interaction.Events;
using TianShu.Cli.Interaction.Projection;
using TianShu.Contracts.Conversations;
using TianShu.Contracts.Primitives;

namespace TianShu.Cli.Tests;

public sealed class ChatPresentationProjectorTests
{
    [Fact]
    public void Project_AssistantTextDelta_KeepsTextInActiveBuffer()
    {
        var projector = new ChatPresentationProjector();

        var projection = projector.Project(AssistantDelta("我来检查目录。"));

        Assert.Empty(projection.CommittedBlocks);
        Assert.Equal("我来检查目录。", projector.State.ActiveAssistantText);
        Assert.True(projector.State.HasActiveAssistantText);
        Assert.Contains(projection.Records, record => record.Kind == "assistant_delta_received");
    }

    [Fact]
    public void Project_ToolStartedAfterAssistantDelta_CommitsAssistantBlockFirst()
    {
        var projector = new ChatPresentationProjector();
        projector.Project(AssistantDelta("先确认目录状态。"));

        var projection = projector.Project(ToolStarted(
            callId: "call-shell-1",
            inputJson: "{\"command\":\"Test-Path C:\\\\Temp\"}"));

        var assistantBlock = Assert.IsType<AssistantMessageBlock>(Assert.Single(projection.CommittedBlocks));
        Assert.Equal("先确认目录状态。", assistantBlock.Text);
        Assert.False(assistantBlock.IsComplete);
        Assert.False(projector.State.HasActiveAssistantText);
        var step = Assert.IsType<AssistantStepItem>(Assert.Single(projector.State.OutputModel.Items));
        Assert.Equal("先确认目录状态。", step.Summary.Text);
        Assert.Empty(step.Tools);
        Assert.Contains(projection.Records, record => record.Kind == "assistant_block_committed" && record.Reason == "before_tool");
        Assert.Contains(projection.Records, record => record.Kind == "tool_input_cached");
    }

    [Fact]
    public void Project_ToolCompletedWithoutInput_InheritsStartedInput()
    {
        var projector = new ChatPresentationProjector();
        projector.Project(ToolStarted(
            callId: "call-shell-1",
            inputJson: "{\"command\":\"Get-Location\"}"));

        var projection = projector.Project(ToolCompleted(
            callId: "call-shell-1",
            outputJson: "{\"output\":\"C:\\\\Users\\\\Example\",\"metadata\":{\"exitCode\":0}}"));

        var toolBlock = Assert.IsType<ToolInvocationBlock>(Assert.Single(projection.CommittedBlocks));
        Assert.Equal(ToolPresentationKind.Command, toolBlock.Kind);
        Assert.Equal("执行命令", toolBlock.TitleText);
        Assert.Equal("Get-Location", toolBlock.SubjectText);
        Assert.Equal(ToolPresentationStatus.Completed, toolBlock.Status);
        var standaloneTool = Assert.IsType<ToolOutputItem>(Assert.Single(projector.State.OutputModel.Items));
        Assert.Equal("Get-Location", standaloneTool.Tool.SubjectText);
        Assert.Contains(projection.Records, record => record.Kind == "tool_block_committed");
    }

    [Fact]
    public void Project_ToolInvocationEvent_UsesTypedPayloadBeforeRawTextFallback()
    {
        var projector = new ChatPresentationProjector();
        projector.Project(new ToolInvocationEvent(
            ThreadId: "thread-1",
            TurnId: "turn-1",
            Timestamp: DateTimeOffset.UnixEpoch,
            ToolName: "shell",
            CallId: "call-shell-typed",
            ItemId: null,
            InputText: "{\"noise\":true}",
            OutputText: null,
            Status: "in_progress",
            Phase: null,
            InvocationPhase: ToolInvocationPhase.Started,
            Payload: new ToolInvocationPayload(
                ToolPresentationKind.Command,
                new ToolInvocationInput(
                    RawText: "{\"noise\":true}",
                    Subject: "typed command",
                    Command: "typed command",
                    Path: null),
                Output: null)));

        var projection = projector.Project(new ToolInvocationEvent(
            ThreadId: "thread-1",
            TurnId: "turn-1",
            Timestamp: DateTimeOffset.UnixEpoch.AddSeconds(1),
            ToolName: "shell",
            CallId: "call-shell-typed",
            ItemId: null,
            InputText: null,
            OutputText: "{\"noise\":true}",
            Status: "completed",
            Phase: null,
            InvocationPhase: ToolInvocationPhase.Completed,
            Payload: new ToolInvocationPayload(
                ToolPresentationKind.Command,
                Input: null,
                Output: new ToolInvocationOutput(
                    RawText: "{\"noise\":true}",
                    Summary: "typed result",
                    ExitCode: 0,
                    DurationSeconds: 1.2))));

        var toolBlock = Assert.IsType<ToolInvocationBlock>(Assert.Single(projection.CommittedBlocks));
        Assert.Equal(ToolPresentationKind.Command, toolBlock.Kind);
        Assert.Equal("typed command", toolBlock.SubjectText);
        Assert.Equal("typed result", toolBlock.Summary);
    }

    [Fact]
    public void Project_ToolCompleted_WhenRuntimeCancellation_NormalizesCancellationSummary()
    {
        var projector = new ChatPresentationProjector();
        projector.Project(ToolStarted(
            callId: "call-shell-cancel",
            inputJson: "{\"command\":\"Start-Sleep 60\"}"));

        var projection = projector.Project(ToolCompleted(
            callId: "call-shell-cancel",
            outputJson: "{\"error\":\"A task was canceled.\"}",
            status: "failed"));

        var toolBlock = Assert.IsType<ToolInvocationBlock>(Assert.Single(projection.CommittedBlocks));
        Assert.Equal("Start-Sleep 60", toolBlock.SubjectText);
        Assert.Equal("工具执行已取消。", toolBlock.Summary);
        Assert.Equal(ToolPresentationStatus.Canceled, toolBlock.Status);
    }

    [Fact]
    public void Project_AssistantStepAndFollowingTool_AreGroupedInProjectorOutputState()
    {
        var projector = new ChatPresentationProjector();
        projector.Project(AssistantDelta("先确认目录状态。"));
        projector.Project(ToolStarted(
            callId: "call-shell-1",
            inputJson: "{\"command\":\"Get-Location\"}"));

        projector.Project(ToolCompleted(
            callId: "call-shell-1",
            outputJson: "{\"output\":\"C:\\\\Users\\\\Example\",\"metadata\":{\"exitCode\":0}}"));

        var step = Assert.IsType<AssistantStepItem>(Assert.Single(projector.State.OutputModel.Items));
        Assert.Equal("先确认目录状态。", step.Summary.Text);
        var tool = Assert.Single(step.Tools);
        Assert.Equal(ToolPresentationKind.Command, tool.Kind);
        Assert.Equal("Get-Location", tool.SubjectText);
    }

    [Fact]
    public void Project_PlanUpdatedAfterAssistantDelta_CommitsAssistantAndUpdatesDockPlan()
    {
        var projector = new ChatPresentationProjector();
        projector.Project(AssistantDelta("我会先列出计划。"));

        var projection = projector.Project(new ControlPlaneConversationStreamEvent
        {
            Kind = ControlPlaneConversationStreamEventKind.PlanUpdated,
            ThreadId = new ThreadId("thread-1"),
            TurnId = new TurnId("turn-1"),
            Text = "创建测试工具",
            PayloadKind = ControlPlaneConversationStreamPayloadKind.Plan,
            Payload = StructuredValueTestHelper.FromJson(
                """
                {
                  "steps": [
                    { "step": "创建项目", "status": "completed" },
                    { "step": "实现界面", "status": "in_progress" }
                  ]
                }
                """),
        });

        var assistantBlock = Assert.IsType<AssistantMessageBlock>(Assert.Single(projection.CommittedBlocks));
        Assert.Equal("我会先列出计划。", assistantBlock.Text);
        Assert.NotNull(projection.Plan);
        Assert.Equal(1, projection.Plan!.CompletedCount);
        Assert.Equal("实现界面", projection.Plan.CurrentStep);
        Assert.Contains(projection.Records, record => record.Kind == "dock_state_updated");
    }

    [Fact]
    public void ClearPlanDockState_BeforeNextTurn_PreventsPreviousPlanFromReturningOnNonPlanEvent()
    {
        var projector = new ChatPresentationProjector();
        projector.Project(PlanUpdated("上一轮计划"));

        Assert.NotNull(projector.State.Plan);

        projector.ClearPlanDockState();
        var projection = projector.Project(AssistantDelta("新一轮开始。"));

        Assert.Null(projector.State.Plan);
        Assert.Null(projection.Plan);
    }

    [Fact]
    public void Project_TurnCompletedAfterAssistantDelta_CommitsAssistantAsComplete()
    {
        var projector = new ChatPresentationProjector();
        projector.Project(AssistantDelta("任务完成。"));

        var projection = projector.Project(new ControlPlaneConversationStreamEvent
        {
            Kind = ControlPlaneConversationStreamEventKind.TurnCompleted,
            ThreadId = new ThreadId("thread-1"),
            TurnId = new TurnId("turn-1"),
            Status = "completed",
        });

        var assistantBlock = Assert.IsType<AssistantMessageBlock>(Assert.Single(projection.CommittedBlocks));
        Assert.True(assistantBlock.IsComplete);
        Assert.Equal("任务完成。", assistantBlock.Text);
        Assert.False(projector.State.HasActiveAssistantText);
    }

    [Fact]
    public void Project_ErrorAfterAssistantDelta_CommitsAssistantAndErrorNotice()
    {
        var projector = new ChatPresentationProjector();
        projector.Project(AssistantDelta("我先调用接口。"));

        var projection = projector.Project(new ControlPlaneConversationStreamEvent
        {
            Kind = ControlPlaneConversationStreamEventKind.Error,
            ThreadId = new ThreadId("thread-1"),
            TurnId = new TurnId("turn-1"),
            Message = "执行失败：provider returned {\"error\":{\"message\":\"model overloaded\"}}",
        });

        Assert.Collection(
            projection.CommittedBlocks,
            block =>
            {
                var assistantBlock = Assert.IsType<AssistantMessageBlock>(block);
                Assert.Equal("我先调用接口。", assistantBlock.Text);
            },
            block =>
            {
                var errorBlock = Assert.IsType<SystemNoticeBlock>(block);
                Assert.Equal("provider returned，model overloaded", errorBlock.Text);
            });
        Assert.Contains(projection.Records, record => record.Kind == "error_block_committed");
    }

    [Fact]
    public void Project_DuplicateToolCompleted_SuppressesSecondVisibleToolBlock()
    {
        var projector = new ChatPresentationProjector();
        projector.Project(ToolStarted(
            callId: "call-shell-1",
            inputJson: "{\"command\":\"Get-Location\"}"));

        var first = projector.Project(ToolCompleted(
            callId: "call-shell-1",
            outputJson: "{\"output\":\"C:\\\\Users\\\\Example\",\"metadata\":{\"exitCode\":0}}"));
        var second = projector.Project(ToolCompleted(
            callId: "call-shell-1",
            outputJson: "{\"output\":\"C:\\\\Users\\\\Example\",\"metadata\":{\"exitCode\":0}}"));

        Assert.IsType<ToolInvocationBlock>(Assert.Single(first.CommittedBlocks));
        Assert.Empty(second.CommittedBlocks);
    }

    [Fact]
    public void Project_ToolHookCompletedBeforeCallCompleted_DoesNotConsumeVisibleCompletion()
    {
        var projector = new ChatPresentationProjector();

        projector.Project(ToolStarted(
            callId: "call-shell-1",
            inputJson: "{\"command\":[\"dotnet\",\"--version\"]}"));
        projector.Project(ToolHookStarted("call-shell-1"));
        projector.Project(ToolHookCompleted("call-shell-1"));
        var projection = projector.Project(ToolCompleted(
            callId: "call-shell-1",
            outputJson: "{\"output\":\"10.0.202\\r\\n\",\"metadata\":{\"exit_code\":0}}"));

        var toolBlock = Assert.IsType<ToolInvocationBlock>(Assert.Single(projection.CommittedBlocks));
        Assert.Equal(ToolPresentationKind.Command, toolBlock.Kind);
        Assert.Equal("执行命令", toolBlock.TitleText);
        Assert.Equal("dotnet --version", toolBlock.SubjectText);
        Assert.Contains("10.0.202", toolBlock.Summary, StringComparison.Ordinal);
        Assert.Contains(projection.Records, record => record.Kind == "tool_block_committed");
    }

    [Fact]
    public void Project_UpdatePlanToolCompleted_WhenSuccessful_DoesNotCreateTranscriptToolBlock()
    {
        var projector = new ChatPresentationProjector();

        var projection = projector.Project(new ControlPlaneConversationStreamEvent
        {
            Kind = ControlPlaneConversationStreamEventKind.ToolCallCompleted,
            ThreadId = new ThreadId("thread-1"),
            TurnId = new TurnId("turn-1"),
            CallId = new CallId("call-plan-1"),
            ToolName = "update_plan",
            PayloadKind = ControlPlaneConversationStreamPayloadKind.ToolCall,
            Payload = StructuredValueTestHelper.FromJson(
                """
                {
                  "toolName": "update_plan",
                  "callId": "call-plan-1",
                  "outputText": "已更新计划",
                  "status": "completed"
                }
                """),
        });

        Assert.Empty(projection.CommittedBlocks);
        Assert.Contains(projection.Records, record => record.Kind == "tool_event_merged");
        Assert.DoesNotContain(projection.Records, record => record.Kind == "tool_block_committed");
    }

    [Fact]
    public void Project_ItemDynamicToolStartedAndCompleted_CommitsVisibleToolBlock()
    {
        var projector = new ChatPresentationProjector();
        projector.Project(new ControlPlaneConversationStreamEvent
        {
            Kind = ControlPlaneConversationStreamEventKind.ItemStarted,
            ThreadId = new ThreadId("thread-1"),
            TurnId = new TurnId("turn-1"),
            ItemId = "tool-shell-1",
            PayloadKind = ControlPlaneConversationStreamPayloadKind.Item,
            Payload = StructuredValueTestHelper.FromJson(
                """
                {
                  "itemId": "tool-shell-1",
                  "itemType": "dynamicToolCall",
                  "toolName": "shell",
                  "callId": "tool-shell-1",
                  "arguments": "{\"command\":\"Get-Location\"}",
                  "status": "inProgress"
                }
                """),
        });

        var projection = projector.Project(new ControlPlaneConversationStreamEvent
        {
            Kind = ControlPlaneConversationStreamEventKind.ItemCompleted,
            ThreadId = new ThreadId("thread-1"),
            TurnId = new TurnId("turn-1"),
            ItemId = "tool-shell-1",
            PayloadKind = ControlPlaneConversationStreamPayloadKind.Item,
            Payload = StructuredValueTestHelper.FromJson(
                """
                {
                  "itemId": "tool-shell-1",
                  "itemType": "dynamicToolCall",
                  "toolName": "shell",
                  "callId": "tool-shell-1",
                  "outputText": "{\"output\":\"C:\\\\Users\\\\Example\",\"metadata\":{\"exitCode\":0}}",
                  "status": "completed"
                }
                """),
        });

        var toolBlock = Assert.IsType<ToolInvocationBlock>(Assert.Single(projection.CommittedBlocks));
        Assert.Equal(ToolPresentationKind.Command, toolBlock.Kind);
        Assert.Equal("执行命令", toolBlock.TitleText);
        Assert.Equal("Get-Location", toolBlock.SubjectText);
        Assert.Equal(ToolPresentationStatus.Completed, toolBlock.Status);
        Assert.Contains(projection.Records, record => record.Kind == "tool_block_committed");
    }

    [Fact]
    public void CliEventNormalizer_ItemDynamicToolStarted_BuildsTypedPayload()
    {
        var interactionEvent = CliEventNormalizer.Normalize(new ControlPlaneConversationStreamEvent
        {
            Kind = ControlPlaneConversationStreamEventKind.ItemStarted,
            ThreadId = new ThreadId("thread-1"),
            TurnId = new TurnId("turn-1"),
            ItemId = "tool-shell-typed",
            PayloadKind = ControlPlaneConversationStreamPayloadKind.Item,
            Payload = StructuredValueTestHelper.FromJson(
                """
                {
                  "itemId": "tool-shell-typed",
                  "itemType": "dynamicToolCall",
                  "toolName": "shell",
                  "callId": "tool-shell-typed",
                  "arguments": "{\"command\":[\"dotnet\",\"--version\"]}",
                  "status": "inProgress"
                }
                """),
        });

        var toolEvent = Assert.IsType<ToolInvocationEvent>(interactionEvent);
        Assert.NotNull(toolEvent.Payload);
        Assert.Equal(ToolPresentationKind.Command, toolEvent.Payload!.Kind);
        Assert.Equal("dotnet --version", toolEvent.Payload.Input?.Command);
        Assert.Equal("dotnet --version", toolEvent.Payload.Input?.Subject);
    }

    [Fact]
    public void CliEventNormalizer_ToolPayloadPresentationKindMetadataWinsBeforeToolNameFallback()
    {
        var interactionEvent = CliEventNormalizer.Normalize(new ControlPlaneConversationStreamEvent
        {
            Kind = ControlPlaneConversationStreamEventKind.ToolCallStarted,
            ThreadId = new ThreadId("thread-1"),
            TurnId = new TurnId("turn-1"),
            CallId = new CallId("call-file-typed"),
            ToolName = "shell",
            PayloadKind = ControlPlaneConversationStreamPayloadKind.ToolCall,
            Payload = StructuredValueTestHelper.FromJson(
                """
                {
                  "toolName": "shell",
                  "callId": "call-file-typed",
                  "presentationKind": "file_change",
                  "inputText": "{\"path\":\"C:\\\\Users\\\\Example\\\\Desktop\\\\TestTianShu\\\\MainWindow.xaml\"}",
                  "status": "in_progress"
                }
                """),
        });

        var toolEvent = Assert.IsType<ToolInvocationEvent>(interactionEvent);
        Assert.NotNull(toolEvent.Payload);
        Assert.Equal(ToolPresentationKind.FileChange, toolEvent.Payload!.Kind);
        Assert.Equal("C:\\Users\\Example\\Desktop\\TestTianShu\\MainWindow.xaml", toolEvent.Payload.Input?.Path);
    }

    [Fact]
    public void CliEventNormalizer_ToolPayloadMetadataObjectCanProvideCanonicalKind()
    {
        var interactionEvent = CliEventNormalizer.Normalize(new ControlPlaneConversationStreamEvent
        {
            Kind = ControlPlaneConversationStreamEventKind.ToolCallStarted,
            ThreadId = new ThreadId("thread-1"),
            TurnId = new TurnId("turn-1"),
            CallId = new CallId("call-plan-typed"),
            ToolName = "unknown_tool",
            PayloadKind = ControlPlaneConversationStreamPayloadKind.ToolCall,
            Payload = StructuredValueTestHelper.FromJson(
                """
                {
                  "toolName": "unknown_tool",
                  "callId": "call-plan-typed",
                  "metadata": {
                    "category": "plan_update"
                  },
                  "inputText": "{\"plan\":[{\"step\":\"整理结构\",\"status\":\"in_progress\"}]}",
                  "status": "in_progress"
                }
                """),
        });

        var toolEvent = Assert.IsType<ToolInvocationEvent>(interactionEvent);
        Assert.NotNull(toolEvent.Payload);
        Assert.Equal(ToolPresentationKind.PlanUpdate, toolEvent.Payload!.Kind);
        Assert.Contains("整理结构", toolEvent.Payload.Input?.Subject, StringComparison.Ordinal);
    }

    [Fact]
    public void CliEventNormalizer_ToolCompletedLifecycleText_DoesNotBecomeOutputSummary()
    {
        var interactionEvent = CliEventNormalizer.Normalize(new ControlPlaneConversationStreamEvent
        {
            Kind = ControlPlaneConversationStreamEventKind.ToolCallCompleted,
            ThreadId = new ThreadId("thread-1"),
            TurnId = new TurnId("turn-1"),
            ItemId = "tool-write-1",
            CallId = new CallId("tool-write-1"),
            ToolName = "fileChange",
            Text = "id=tool-write-1, type=fileChange, status=completed",
            PayloadKind = ControlPlaneConversationStreamPayloadKind.ToolCall,
            Payload = StructuredValueTestHelper.FromJson(
                """
                {
                  "toolName": "fileChange",
                  "callId": "tool-write-1",
                  "inputText": "{\"path\":\"C:\\\\Users\\\\Example\\\\Desktop\\\\TestTianShu\\\\HttpTester\\\\MainWindow.xaml\"}",
                  "status": "completed"
                }
                """),
        });

        var toolEvent = Assert.IsType<ToolInvocationEvent>(interactionEvent);
        Assert.Equal(ToolPresentationKind.FileChange, toolEvent.Payload!.Kind);
        Assert.Equal("C:\\Users\\Example\\Desktop\\TestTianShu\\HttpTester\\MainWindow.xaml", toolEvent.Payload.Input?.Subject);
        Assert.Null(toolEvent.OutputText);
        Assert.Null(toolEvent.Payload.Output);
    }

    [Fact]
    public void Project_ItemFileChangeWithTypePayload_CommitsModifyFileBlock()
    {
        var projector = new ChatPresentationProjector();
        projector.Project(AssistantDelta("设计一个外观现代化的 HTTP 测试工具界面。"));

        var projection = projector.Project(new ControlPlaneConversationStreamEvent
        {
            Kind = ControlPlaneConversationStreamEventKind.ItemCompleted,
            ThreadId = new ThreadId("thread-1"),
            TurnId = new TurnId("turn-1"),
            ItemId = "tool_write_call_00_etPMVKZYzXdKTwxSkQQJ0208",
            Text = "id=tool_write_call_00_etPMVKZYzXdKTwxSkQQJ0208, type=fileChange, status=completed",
            PayloadKind = ControlPlaneConversationStreamPayloadKind.Item,
            Payload = StructuredValueTestHelper.FromJson(
                """
                {
                  "id": "tool_write_call_00_etPMVKZYzXdKTwxSkQQJ0208",
                  "type": "fileChange",
                  "changes": [
                    {
                      "path": "C:\\Users\\Example\\Desktop\\TestTianShu\\HttpTester\\MainWindow.xaml",
                      "kind": "update",
                      "diff": "<Window />"
                    }
                  ],
                  "status": "completed"
                }
                """),
        });

        Assert.Collection(
            projection.CommittedBlocks,
            block =>
            {
                var assistantBlock = Assert.IsType<AssistantMessageBlock>(block);
                Assert.Equal("设计一个外观现代化的 HTTP 测试工具界面。", assistantBlock.Text);
            },
            block =>
            {
                var toolBlock = Assert.IsType<ToolInvocationBlock>(block);
                Assert.Equal(ToolPresentationKind.FileChange, toolBlock.Kind);
                Assert.Equal("修改文件", toolBlock.TitleText);
                Assert.Equal("C:\\Users\\Example\\Desktop\\TestTianShu\\HttpTester\\MainWindow.xaml", toolBlock.SubjectText);
                Assert.Null(toolBlock.Summary);
                Assert.Equal(ToolPresentationStatus.Completed, toolBlock.Status);
            });
        Assert.Contains(projection.Records, record => record.Kind == "tool_block_committed");
    }

    [Fact]
    public void CliEventNormalizer_ToolFileChangeRawItemPayload_ReadsFirstChangePath()
    {
        var interactionEvent = CliEventNormalizer.Normalize(new ControlPlaneConversationStreamEvent
        {
            Kind = ControlPlaneConversationStreamEventKind.ToolCallCompleted,
            ThreadId = new ThreadId("thread-1"),
            TurnId = new TurnId("turn-1"),
            ItemId = "tool-write-1",
            ToolName = "fileChange",
            CallId = new CallId("tool-write-1"),
            PayloadKind = ControlPlaneConversationStreamPayloadKind.ToolCall,
            Payload = StructuredValueTestHelper.FromJson(
                """
                {
                  "toolName": "fileChange",
                  "callId": "tool-write-1",
                  "inputText": "{\"id\":\"tool-write-1\",\"type\":\"fileChange\",\"changes\":[{\"path\":\"src/App.xaml\",\"kind\":\"update\"}],\"status\":\"completed\"}",
                  "status": "completed"
                }
                """),
        });

        var toolEvent = Assert.IsType<ToolInvocationEvent>(interactionEvent);
        Assert.Equal(ToolPresentationKind.FileChange, toolEvent.Payload!.Kind);
        Assert.Equal("src/App.xaml", toolEvent.Payload.Input?.Path);
        Assert.Equal("src/App.xaml", toolEvent.Payload.Input?.Subject);
    }

    [Fact]
    public void CliEventNormalizer_ItemWebSearchCompleted_BuildsVisibleTypedPayload()
    {
        var interactionEvent = CliEventNormalizer.Normalize(new ControlPlaneConversationStreamEvent
        {
            Kind = ControlPlaneConversationStreamEventKind.ItemCompleted,
            ThreadId = new ThreadId("thread-1"),
            TurnId = new TurnId("turn-1"),
            ItemId = "web-search-1",
            PayloadKind = ControlPlaneConversationStreamPayloadKind.Item,
            Payload = StructuredValueTestHelper.FromJson(
                """
                {
                  "itemId": "web-search-1",
                  "type": "webSearch",
                  "query": "TianShu CLI typed item policy",
                  "status": "completed"
                }
                """),
        });

        var toolEvent = Assert.IsType<ToolInvocationEvent>(interactionEvent);
        Assert.Equal("webSearch", toolEvent.ToolName);
        Assert.Equal(ToolPresentationKind.WebSearch, toolEvent.Payload!.Kind);
        Assert.Equal("TianShu CLI typed item policy", toolEvent.Payload.Input?.Subject);
    }

    [Fact]
    public void Project_ItemImageGenerationCompleted_CommitsVisibleToolBlock()
    {
        var projector = new ChatPresentationProjector();

        var projection = projector.Project(new ControlPlaneConversationStreamEvent
        {
            Kind = ControlPlaneConversationStreamEventKind.ItemCompleted,
            ThreadId = new ThreadId("thread-1"),
            TurnId = new TurnId("turn-1"),
            ItemId = "image-generation-1",
            PayloadKind = ControlPlaneConversationStreamPayloadKind.Item,
            Payload = StructuredValueTestHelper.FromJson(
                """
                {
                  "itemId": "image-generation-1",
                  "type": "imageGeneration",
                  "savedPath": "C:\\Users\\Example\\Desktop\\image-generation-1.png",
                  "status": "completed"
                }
                """),
        });

        var toolBlock = Assert.IsType<ToolInvocationBlock>(Assert.Single(projection.CommittedBlocks));
        Assert.Equal(ToolPresentationKind.ImageGeneration, toolBlock.Kind);
        Assert.Equal("生成图片", toolBlock.TitleText);
        Assert.Equal("C:\\Users\\Example\\Desktop\\image-generation-1.png", toolBlock.SubjectText);
        Assert.Null(toolBlock.Summary);
        Assert.Equal(ToolPresentationStatus.Completed, toolBlock.Status);
    }

    [Fact]
    public void CliEventNormalizer_ContextCompactionItem_DoesNotEnterVisibleToolProjection()
    {
        var interactionEvent = CliEventNormalizer.Normalize(new ControlPlaneConversationStreamEvent
        {
            Kind = ControlPlaneConversationStreamEventKind.ItemCompleted,
            ThreadId = new ThreadId("thread-1"),
            TurnId = new TurnId("turn-1"),
            ItemId = "compact-1",
            PayloadKind = ControlPlaneConversationStreamPayloadKind.Item,
            Payload = StructuredValueTestHelper.FromJson(
                """
                {
                  "itemId": "compact-1",
                  "type": "contextCompaction",
                  "status": "completed"
                }
                """),
        });

        Assert.IsType<PassthroughInteractionEvent>(interactionEvent);
    }

    private static ControlPlaneConversationStreamEvent AssistantDelta(string text)
        => new()
        {
            Kind = ControlPlaneConversationStreamEventKind.AssistantTextDelta,
            ThreadId = new ThreadId("thread-1"),
            TurnId = new TurnId("turn-1"),
            Text = text,
        };

    private static ControlPlaneConversationStreamEvent PlanUpdated(string explanation)
        => new()
        {
            Kind = ControlPlaneConversationStreamEventKind.PlanUpdated,
            ThreadId = new ThreadId("thread-1"),
            TurnId = new TurnId("turn-1"),
            Text = explanation,
            PayloadKind = ControlPlaneConversationStreamPayloadKind.Plan,
            Payload = StructuredValueTestHelper.FromJson(
                """
                {
                  "steps": [
                    { "step": "创建项目", "status": "completed" },
                    { "step": "实现界面", "status": "in_progress" }
                  ]
                }
                """),
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
                  "inputText": {{ToJsonString(inputJson)}},
                  "status": "in_progress"
                }
                """),
        };

    private static ControlPlaneConversationStreamEvent ToolHookStarted(string callId)
        => new()
        {
            Kind = ControlPlaneConversationStreamEventKind.ToolCallStarted,
            ThreadId = new ThreadId("thread-1"),
            TurnId = new TurnId("turn-1"),
            CallId = new CallId(callId),
            ToolName = "shell",
            Status = "hook",
            PayloadKind = ControlPlaneConversationStreamPayloadKind.ToolCall,
            Payload = StructuredValueTestHelper.FromJson(
                $$"""
                {
                  "toolName": "shell",
                  "callId": "{{callId}}",
                  "status": "hook",
                  "phase": "before"
                }
                """),
        };

    private static ControlPlaneConversationStreamEvent ToolHookCompleted(string callId)
        => new()
        {
            Kind = ControlPlaneConversationStreamEventKind.ToolCallCompleted,
            ThreadId = new ThreadId("thread-1"),
            TurnId = new TurnId("turn-1"),
            CallId = new CallId(callId),
            ToolName = "shell",
            Status = "completed",
            PayloadKind = ControlPlaneConversationStreamPayloadKind.ToolCall,
            Payload = StructuredValueTestHelper.FromJson(
                $$"""
                {
                  "toolName": "shell",
                  "callId": "{{callId}}",
                  "status": "completed",
                  "phase": "after"
                }
                """),
        };

    private static ControlPlaneConversationStreamEvent ToolCompleted(string callId, string outputJson, string status = "completed")
        => new()
        {
            Kind = ControlPlaneConversationStreamEventKind.ToolCallCompleted,
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
                  "outputText": {{ToJsonString(outputJson)}},
                  "status": "{{status}}"
                }
                """),
        };

    private static string ToJsonString(string value)
        => System.Text.Json.JsonSerializer.Serialize(value);
}
