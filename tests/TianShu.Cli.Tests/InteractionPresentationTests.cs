using System.Globalization;
using System.Text.RegularExpressions;
using TianShu.Cli.Interaction.Presenters;
using TianShu.Cli.Interaction.Projection;
using TianShu.Cli.Interaction.Rendering;
using TianShu.Cli.Interaction;
using TianShu.Cli.Terminal;
using TianShu.Contracts.Conversations;

namespace TianShu.Cli.Tests;

public sealed partial class InteractionPresentationTests
{
    [Fact]
    public void ToolInvocationPresenter_SummarizesShellCommandWithoutRawJsonNoise()
    {
        var block = ToolInvocationPresenter.BuildCompleted(
            "shell_command",
            "{\"command\":\"Get-Location\",\"output\":true}",
            "{\"output\":\"C:\\\\Users\\\\Example\",\"metadata\":{\"exitCode\":0}}",
            "completed");

        Assert.NotNull(block);
        var rendered = new TerminalTranscriptRenderer().Render(block, styled: false);

        Assert.Contains("● 执行命令", rendered, StringComparison.Ordinal);
        Assert.Contains("  Get-Location", rendered, StringComparison.Ordinal);
        Assert.Contains("  ✓ C:\\Users\\Example", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("output=True", rendered, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("{\"output\"", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void TerminalTranscriptRenderer_StyledToolBlock_ColorsTitleAndResultSeparately()
    {
        var commandBlock = new ToolInvocationBlock(
            ToolPresentationKind.Command,
            new ToolInvocationTitle("执行命令"),
            new ToolInvocationSubject("dotnet build", AlwaysShow: true),
            new ToolInvocationResult("完成，退出码 0"),
            ToolPresentationStatus.Completed);
        var fileBlock = new ToolInvocationBlock(
            ToolPresentationKind.FileChange,
            new ToolInvocationTitle("修改文件"),
            new ToolInvocationSubject("MainWindow.xaml"),
            new ToolInvocationResult("写入成功：MainWindow.xaml"),
            ToolPresentationStatus.Completed);
        var failedBlock = new ToolInvocationBlock(
            ToolPresentationKind.Command,
            new ToolInvocationTitle("执行命令"),
            new ToolInvocationSubject("dotnet build", AlwaysShow: true),
            new ToolInvocationResult("error MC1000"),
            ToolPresentationStatus.Failed);
        var canceledBlock = new ToolInvocationBlock(
            ToolPresentationKind.Command,
            new ToolInvocationTitle("执行命令"),
            new ToolInvocationSubject("Start-Sleep 60", AlwaysShow: true),
            new ToolInvocationResult("工具执行已取消。"),
            ToolPresentationStatus.Canceled);

        var commandRendered = new TerminalTranscriptRenderer().Render(commandBlock, styled: true);
        var fileRendered = new TerminalTranscriptRenderer().Render(fileBlock, styled: true);
        var failedRendered = new TerminalTranscriptRenderer().Render(failedBlock, styled: true);
        var canceledRendered = new TerminalTranscriptRenderer().Render(canceledBlock, styled: true);

        Assert.Contains(TerminalAnsi.YellowText("● 执行命令"), commandRendered, StringComparison.Ordinal);
        Assert.Contains(TerminalAnsi.BlueText("● 修改文件"), fileRendered, StringComparison.Ordinal);
        Assert.Contains(TerminalAnsi.GreenText("  ✓ 完成，退出码 0"), commandRendered, StringComparison.Ordinal);
        Assert.Contains(TerminalAnsi.RedText("  ✗ error MC1000"), failedRendered, StringComparison.Ordinal);
        Assert.Contains(TerminalAnsi.YellowText("  ⊘ 工具执行已取消。"), canceledRendered, StringComparison.Ordinal);
        Assert.DoesNotContain(TerminalAnsi.GreenText("● 执行命令"), commandRendered, StringComparison.Ordinal);
    }

    [Fact]
    public void TerminalTranscriptRenderer_StyledToolBlock_UsesKindInsteadOfVisibleTitle()
    {
        var commandBlock = new ToolInvocationBlock(
            ToolPresentationKind.Command,
            new ToolInvocationTitle("运行动作"),
            new ToolInvocationSubject("Get-Location", AlwaysShow: true),
            new ToolInvocationResult("Get-Location -> C:\\Users\\Example"),
            ToolPresentationStatus.Completed);
        var fileBlock = new ToolInvocationBlock(
            ToolPresentationKind.FileChange,
            new ToolInvocationTitle("执行命令"),
            new ToolInvocationSubject("MainWindow.xaml"),
            new ToolInvocationResult("写入成功：MainWindow.xaml"),
            ToolPresentationStatus.Completed);

        var commandRendered = new TerminalTranscriptRenderer().Render(commandBlock, styled: true);
        var fileRendered = new TerminalTranscriptRenderer().Render(fileBlock, styled: true);

        Assert.Contains(TerminalAnsi.YellowText("● 运行动作"), commandRendered, StringComparison.Ordinal);
        Assert.Contains("  Get-Location", commandRendered, StringComparison.Ordinal);
        Assert.Contains(TerminalAnsi.BlueText("● 执行命令"), fileRendered, StringComparison.Ordinal);
        Assert.DoesNotContain(TerminalAnsi.YellowText("● 执行命令"), fileRendered, StringComparison.Ordinal);
    }

    [Fact]
    public void ConversationOutputModel_GroupsAssistantSummaryWithFollowingToolInvocation()
    {
        var model = ConversationOutputModel.FromBlocks(
            [
                new AssistantMessageBlock("我先检查目录。", IsComplete: false),
                new ToolInvocationBlock(
                    ToolPresentationKind.Command,
                    new ToolInvocationTitle("执行命令"),
                    new ToolInvocationSubject("Get-Location", AlwaysShow: true),
                    new ToolInvocationResult("C:\\Users\\Example"),
                    ToolPresentationStatus.Completed),
                new SystemNoticeBlock("完成。"),
            ]);

        Assert.Collection(
            model.Items,
            item =>
            {
                var step = Assert.IsType<AssistantStepItem>(item);
                Assert.Equal("我先检查目录。", step.Summary.Text);
                var tool = Assert.Single(step.Tools);
                Assert.Equal(ToolPresentationKind.Command, tool.Kind);
                Assert.Equal("Get-Location", tool.SubjectText);
            },
            item =>
            {
                var notice = Assert.IsType<NoticeOutputItem>(item);
                Assert.Equal("完成。", notice.Notice.Text);
            });
    }

    [Fact]
    public void ComposerDockState_ExposesTypedInputStatusAndPlanModels()
    {
        var state = new ComposerDockState(
            InputText: "hello",
            Cursor: 5,
            Prompt: "> ",
            Plan: new PlanDockSummary(
                1,
                2,
                "实现界面",
                "创建工具",
                [new PlanDockStep("1", "创建项目", PlanDockStepStatus.Completed)]),
            Agents: new AgentDockSummary(0),
            Model: new ModelDockSummary("deepseek-chat"),
            IsBusy: true,
            WorkingElapsed: TimeSpan.FromSeconds(3));

        Assert.Equal("hello", state.Input.Text);
        Assert.True(state.StatusBar.IsBusy);
        Assert.Equal("deepseek-chat", state.StatusBar.Model?.Model);
        Assert.NotNull(state.PlanPanel);
        Assert.Equal("实现界面", state.PlanPanel!.CurrentStep);
    }

    [Fact]
    public void ComposerDockRenderer_CommandOverlay_ReplacesInputStatusAndPlanDock()
    {
        var state = new ComposerDockState(
            InputText: "/model-route status",
            Cursor: 19,
            Prompt: "> ",
            Plan: new PlanDockSummary(
                0,
                1,
                "正在执行",
                "测试计划",
                [new PlanDockStep("1", "调用工具", PlanDockStepStatus.Running)]),
            Agents: new AgentDockSummary(0),
            Model: new ModelDockSummary("openai-compatible-default"),
            IsBusy: true,
            WorkingElapsed: TimeSpan.FromSeconds(3),
            QueuedFollowUps: new QueuedFollowUpDockState(1, [new QueuedFollowUpDockEntryState(1, "排队消息")]),
            InputNotice: "确认提示",
            CommandOverlay: new CommandOverlayDockState(["模型状态", "openai-compatible-default testing"]));

        var lines = new ComposerDockRenderer()
            .BuildDockLines(state, popupLines: ["/model-route status"], styled: false, width: 80)
            .Select(StripAnsi)
            .ToArray();

        Assert.Contains("模型状态", lines[1], StringComparison.Ordinal);
        Assert.Contains("openai-compatible-default testing", lines[2], StringComparison.Ordinal);
        Assert.DoesNotContain(lines, line => line.Contains("> /model-route status", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Contains("待发送队列", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Contains("Working", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Contains("调用工具", StringComparison.Ordinal));
        Assert.Equal(0, ComposerDockRenderer.CalculateInputLineIndex(state, popupLines: ["/model-route status"], width: 80));
    }

    [Fact]
    public void ToolInvocationPresenter_SummarizesExecCommandAsShellCommand()
    {
        var block = ToolInvocationPresenter.BuildCompleted(
            "exec_command",
            "{\"cmd\":\"Get-ChildItem | Select-Object -ExpandProperty FullName\"}",
            "{\"exitCode\":0,\"durationSeconds\":0.2}",
            "completed");

        Assert.NotNull(block);
        var rendered = new TerminalTranscriptRenderer().Render(block, styled: false);

        Assert.Contains("● 执行命令", rendered, StringComparison.Ordinal);
        Assert.Contains("  Get-ChildItem | Select-Object -ExpandProperty FullName", rendered, StringComparison.Ordinal);
        Assert.Contains("  ✓ 完成，退出码 0", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void ToolInvocationPresenter_SummarizesCommandArrayAsShellCommand()
    {
        var block = ToolInvocationPresenter.BuildCompleted(
            "shell",
            "{\"command\":[\"dotnet\",\"new\",\"wpf\",\"-n\",\"HttpTester\"]}",
            "{\"exitCode\":0,\"durationSeconds\":0.3}",
            "completed");

        Assert.NotNull(block);
        var rendered = new TerminalTranscriptRenderer().Render(block, styled: false);

        Assert.Contains("● 执行命令", rendered, StringComparison.Ordinal);
        Assert.Contains("  dotnet new wpf -n HttpTester", rendered, StringComparison.Ordinal);
        Assert.Contains("  ✓ 完成，退出码 0", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void ToolInvocationPresenter_SummarizesNestedCommandObjectAsShellCommand()
    {
        var block = ToolInvocationPresenter.BuildCompleted(
            "shell",
            "{\"arguments\":{\"command\":[\"dotnet\",\"new\",\"wpf\",\"-n\",\"HttpTester\"]}}",
            "{\"exitCode\":0,\"durationSeconds\":0.3}",
            "completed");

        Assert.NotNull(block);
        var rendered = new TerminalTranscriptRenderer().Render(block, styled: false);

        Assert.Contains("● 执行命令", rendered, StringComparison.Ordinal);
        Assert.Contains("  dotnet new wpf -n HttpTester", rendered, StringComparison.Ordinal);
        Assert.Contains("  ✓ 完成，退出码 0", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void ToolInvocationPresenter_SummarizesNestedCommandJsonStringAsShellCommand()
    {
        var block = ToolInvocationPresenter.BuildCompleted(
            "shell",
            "{\"arguments\":\"{\\\"command\\\":[\\\"dotnet\\\",\\\"new\\\",\\\"wpf\\\",\\\"-n\\\",\\\"HttpTester\\\"]}\"}",
            "{\"exitCode\":0,\"durationSeconds\":0.3}",
            "completed");

        Assert.NotNull(block);
        var rendered = new TerminalTranscriptRenderer().Render(block, styled: false);

        Assert.Contains("● 执行命令", rendered, StringComparison.Ordinal);
        Assert.Contains("  dotnet new wpf -n HttpTester", rendered, StringComparison.Ordinal);
        Assert.Contains("  ✓ 完成，退出码 0", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void ToolInvocationSummaryBuilder_ShellSummaryUsesNestedCommandPresenterLogic()
    {
        var summary = ToolInvocationSummaryBuilder.BuildInputSummary(
            "shell",
            "{\"input\":{\"command\":[\"dotnet\",\"new\",\"wpf\",\"-n\",\"HttpTester\"]}}");

        Assert.Equal("dotnet new wpf -n HttpTester", summary);
    }

    [Fact]
    public void ToolInvocationSummaryBuilder_UpdatePlanInputSummaryIncludesSteps()
    {
        var summary = ToolInvocationSummaryBuilder.BuildInputSummary(
            "update_plan",
            """
            {
              "explanation": "开始 CLI 分层迁移",
              "plan": [
                { "step": "迁出工具摘要", "status": "completed" },
                { "step": "迁出计划摘要", "status": "in_progress" }
              ]
            }
            """);

        Assert.NotNull(summary);
        Assert.Contains("开始 CLI 分层迁移", summary, StringComparison.Ordinal);
        Assert.Contains("1. 迁出工具摘要 [completed]", summary, StringComparison.Ordinal);
        Assert.Contains("2. 迁出计划摘要 [in_progress]", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void ToolInvocationSummaryBuilder_OutputSummaryIncludesExitCodeAndDuration()
    {
        var summary = ToolInvocationSummaryBuilder.BuildOutputSummary(
            "shell",
            "{\"metadata\":{\"exitCode\":1,\"durationSeconds\":12.4}}",
            "failed");

        Assert.Equal("失败，退出码 1，耗时 12s", summary);
    }

    [Fact]
    public void PlanUpdateSummaryBuilder_BuildsDisplayTextFromStructuredPlanPayload()
    {
        var streamEvent = new ControlPlaneConversationStreamEvent
        {
            Kind = ControlPlaneConversationStreamEventKind.PlanUpdated,
            Text = "迁移 CLI 展示摘要",
            PayloadKind = ControlPlaneConversationStreamPayloadKind.Plan,
            Payload = StructuredValueTestHelper.FromJson(
                """
                {
                  "explanation": "收口 Runner 摘要逻辑",
                  "steps": [
                    { "step": "迁出工具摘要", "status": "completed" },
                    { "step": "迁出计划摘要", "status": "in_progress" },
                    { "step": "补齐测试", "status": "pending" }
                  ]
                }
                """),
        };

        var text = PlanUpdateSummaryBuilder.BuildDisplayText(streamEvent);

        Assert.NotNull(text);
        Assert.Contains("- 更新计划：收口 Runner 摘要逻辑 (1/3 完成，当前：迁出计划摘要)", text, StringComparison.Ordinal);
        Assert.Contains("  [x] 1. 迁出工具摘要 [完成]", text, StringComparison.Ordinal);
        Assert.Contains("  [>] 2. 迁出计划摘要 [进行中]", text, StringComparison.Ordinal);
        Assert.Contains("  [ ] 3. 补齐测试 [待处理]", text, StringComparison.Ordinal);
    }

    [Fact]
    public void PlanProgressPresenter_BuildsTranscriptBlockAndPlanDockStateProjectorBuildsSummary()
    {
        var payload = StructuredValueTestHelper.FromJson(
            """
            {
              "steps": [
                { "step": "整理 CLI 表现层目录", "status": "completed" },
                { "step": "接入 Composer Dock", "status": "in_progress" },
                { "step": "补齐回归测试", "status": "pending" }
              ]
            }
            """);

        var block = PlanProgressPresenter.FromPayload("CLI 交互首版", payload);
        var summary = PlanDockStateProjector.BuildSummary(block);
        var rendered = new TerminalTranscriptRenderer().Render(block!, styled: false);

        Assert.NotNull(block);
        Assert.NotNull(summary);
        Assert.Equal(1, summary.CompletedCount);
        Assert.Equal(3, summary.TotalCount);
        Assert.Equal("接入 Composer Dock", summary.CurrentStep);
        Assert.NotNull(summary.Steps);
        Assert.Collection(
            summary.Steps!,
            step =>
            {
                Assert.Equal("1", step.Sequence);
                Assert.Equal("整理 CLI 表现层目录", step.Text);
                Assert.Equal(PlanDockStepStatus.Completed, step.Status);
            },
            step =>
            {
                Assert.Equal("2", step.Sequence);
                Assert.Equal("接入 Composer Dock", step.Text);
                Assert.Equal(PlanDockStepStatus.Running, step.Status);
            },
            step =>
            {
                Assert.Equal("3", step.Sequence);
                Assert.Equal("补齐回归测试", step.Text);
                Assert.Equal(PlanDockStepStatus.Pending, step.Status);
            });
        Assert.Contains("● 计划更新  1/3 完成  CLI 交互首版", rendered, StringComparison.Ordinal);
        Assert.Contains("  ✓ 1. 整理 CLI 表现层目录", rendered, StringComparison.Ordinal);
        Assert.Contains("  ▶ 2. 接入 Composer Dock", rendered, StringComparison.Ordinal);
        Assert.Contains("  □ 3. 补齐回归测试", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void PlanProgressRenderer_WhenStyled_KeepsTitleAsPlainAssistantText()
    {
        var payload = StructuredValueTestHelper.FromJson(
            """
            {
              "steps": [
                { "step": "构建并验证项目", "status": "in_progress" }
              ]
            }
            """);

        var block = PlanProgressPresenter.FromPayload("所有源文件已创建，接下来构建验证项目", payload);
        var rendered = new TerminalTranscriptRenderer().Render(block!, styled: true);

        Assert.Contains(
            TerminalAnsi.DimText(TerminalAnsi.GreenText("● 计划更新  0/1 完成")) + "  所有源文件已创建，接下来构建验证项目",
            rendered,
            StringComparison.Ordinal);
        Assert.Contains(
            TerminalAnsi.DimText(TerminalAnsi.GreenText("  ▶ 1. 构建并验证项目")),
            rendered,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ErrorNoticePresenter_ExtractsProviderErrorMessage()
    {
        var message = ErrorNoticePresenter.NormalizeMessage(
            """
            执行失败：provider returned {"error":{"message":"This model is not available"}}
            """);

        Assert.Equal("provider returned，This model is not available", message);
    }

    [Fact]
    public void ComposerDockRenderer_StatusLineKeepsPersistentSummariesOnly()
    {
        var state = new ComposerDockState(
            InputText: "hello",
            Cursor: 5,
            Prompt: "> ",
            Plan: new PlanDockSummary(1, 4, "编写 MainWindow 界面"),
            Agents: new AgentDockSummary(0),
            Model: new ModelDockSummary("deepseek-chat"),
            IsBusy: true);

        var line = ComposerDockRenderer.BuildStatusLine(state, styled: false);

        Assert.Contains("• Working...", line, StringComparison.Ordinal);
        Assert.Contains("Plan 1/4", line, StringComparison.Ordinal);
        Assert.Contains("▶ 编写 MainWindow 界面", line, StringComparison.Ordinal);
        Assert.Contains("Agents: 0 running", line, StringComparison.Ordinal);
        Assert.Contains("Model: deepseek-chat", line, StringComparison.Ordinal);
        Assert.DoesNotContain("Tool", line, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("执行命令", line, StringComparison.Ordinal);
    }

    [Fact]
    public void ComposerDockRenderer_BuildsInputSeparatorAndStatusLines()
    {
        var state = new ComposerDockState(
            InputText: "/interrupt",
            Cursor: 10,
            Prompt: "> ",
            Plan: null,
            Agents: new AgentDockSummary(1),
            Model: new ModelDockSummary("gpt-5.4"),
            IsBusy: true);

        var lines = new ComposerDockRenderer().BuildDockLines(
            state,
            ["  /interrupt  中断当前回合"],
            styled: false,
            width: 96);

        Assert.Equal(4, lines.Count);
        Assert.StartsWith("> /interrupt", lines[0], StringComparison.Ordinal);
        Assert.StartsWith("────────────────", lines[1], StringComparison.Ordinal);
        Assert.StartsWith("  /interrupt", lines[2], StringComparison.Ordinal);
        Assert.Contains("• Working...", lines[3], StringComparison.Ordinal);
        Assert.Contains("Agents: 1 running", lines[3], StringComparison.Ordinal);
        Assert.Contains("Model: gpt-5.4", lines[3], StringComparison.Ordinal);
    }

    [Fact]
    public void ComposerDockRenderer_BuildsPromptFooterWithSeparatorBeforeStatus()
    {
        var state = new ComposerDockState(
            InputText: "/interrupt",
            Cursor: 10,
            Prompt: "> ",
            Plan: null,
            Agents: new AgentDockSummary(1),
            Model: new ModelDockSummary("gpt-5.4"),
            IsBusy: true);

        var lines = ComposerDockRenderer.BuildPromptFooterLines(state, styled: false, width: 96);

        Assert.Equal(2, lines.Count);
        Assert.StartsWith("────────────────", lines[0], StringComparison.Ordinal);
        Assert.Contains("• Working...", lines[1], StringComparison.Ordinal);
        Assert.Contains("Agents: 1 running", lines[1], StringComparison.Ordinal);
        Assert.Contains("Model: gpt-5.4", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void ComposerDockRenderer_BuildsQueuedFollowUpLinesBelowInputSeparator()
    {
        var state = new ComposerDockState(
            InputText: "继续输入",
            Cursor: 4,
            Prompt: "> ",
            Plan: null,
            Agents: new AgentDockSummary(0),
            Model: new ModelDockSummary("deepseek-chat"),
            IsBusy: true,
            QueuedFollowUps: new QueuedFollowUpDockState(1, [new QueuedFollowUpDockEntryState(1, "排队消息")]));

        var lines = new ComposerDockRenderer().BuildDockLines(
            state,
            popupLines: null,
            styled: false,
            width: 96);

        Assert.StartsWith("> 继续输入", lines[0], StringComparison.Ordinal);
        Assert.StartsWith("────────────────", lines[1], StringComparison.Ordinal);
        Assert.StartsWith("待发送队列：1 条", lines[2], StringComparison.Ordinal);
        Assert.Contains("/follow-up promote <编号>", lines[2], StringComparison.Ordinal);
        Assert.Contains("[1]", lines[3], StringComparison.Ordinal);
        Assert.Contains("排队消息", lines[3], StringComparison.Ordinal);
        Assert.StartsWith("────────────────", lines[4], StringComparison.Ordinal);
        Assert.Contains("• Working...", lines[5], StringComparison.Ordinal);
    }

    [Fact]
    public void ComposerDockRenderer_PlacesPopupBetweenInputSeparatorAndQueuedFollowUps()
    {
        var state = new ComposerDockState(
            InputText: "/",
            Cursor: 1,
            Prompt: "> ",
            Plan: null,
            Agents: new AgentDockSummary(0),
            Model: new ModelDockSummary("deepseek-chat"),
            IsBusy: true,
            QueuedFollowUps: new QueuedFollowUpDockState(1, [new QueuedFollowUpDockEntryState(1, "排队消息")]));

        var lines = new ComposerDockRenderer().BuildDockLines(
            state,
            popupLines: ["  /approve  提交 accept 审批响应"],
            styled: false,
            width: 96);

        Assert.StartsWith("> /", lines[0], StringComparison.Ordinal);
        Assert.StartsWith("────────────────", lines[1], StringComparison.Ordinal);
        Assert.StartsWith("  /approve", lines[2], StringComparison.Ordinal);
        Assert.StartsWith("待发送队列：1 条", lines[3], StringComparison.Ordinal);
        Assert.Contains("[1] 排队消息", lines[4], StringComparison.Ordinal);
        Assert.StartsWith("────────────────", lines[5], StringComparison.Ordinal);
    }

    [Fact]
    public void ComposerDockRenderer_KeepsInputNoticeAboveInputAndQueuedFollowUpsBelowSeparator()
    {
        var state = new ComposerDockState(
            InputText: string.Empty,
            Cursor: 0,
            Prompt: "> ",
            Plan: null,
            Agents: new AgentDockSummary(0),
            Model: new ModelDockSummary("deepseek-chat"),
            IsBusy: false,
            QueuedFollowUps: new QueuedFollowUpDockState(1, [new QueuedFollowUpDockEntryState(1, "排队消息")]),
            InputNotice: "将永久删除全部线程及全部会话日志，包括当前会话。请输入 DELETE ALL THREADS 确认：");

        var lines = new ComposerDockRenderer().BuildDockLines(
            state,
            popupLines: null,
            styled: false,
            width: 120);

        Assert.StartsWith("确认提示：将永久删除全部线程", lines[0], StringComparison.Ordinal);
        Assert.StartsWith("确认文本：", lines[1], StringComparison.Ordinal);
        Assert.StartsWith("DELETE ALL THREADS", lines[2], StringComparison.Ordinal);
        Assert.StartsWith("> ", lines[3], StringComparison.Ordinal);
        Assert.StartsWith("────────────────", lines[4], StringComparison.Ordinal);
        Assert.Contains("待发送队列：1 条", lines[5], StringComparison.Ordinal);
        Assert.Contains("[1] 排队消息", lines[6], StringComparison.Ordinal);
        Assert.StartsWith("────────────────", lines[7], StringComparison.Ordinal);
    }

    [Fact]
    public void ComposerDockRenderer_MarksSelectedQueuedFollowUp()
    {
        var state = new ComposerDockState(
            InputText: string.Empty,
            Cursor: 0,
            Prompt: "> ",
            Plan: null,
            Agents: new AgentDockSummary(0),
            Model: new ModelDockSummary("deepseek-chat"),
            IsBusy: true,
            QueuedFollowUps: new QueuedFollowUpDockState(
                2,
                [
                    new QueuedFollowUpDockEntryState(1, "第一条"),
                    new QueuedFollowUpDockEntryState(2, "第二条", IsSelected: true),
                ],
                SelectedIndex: 2));

        var lines = new ComposerDockRenderer().BuildDockLines(
            state,
            popupLines: null,
            styled: false,
            width: 96);

        Assert.Contains(lines, line => line.StartsWith("  > [2] 第二条", StringComparison.Ordinal));
    }

    [Fact]
    public void ComposerDockRenderer_BuildsPlanPanelAtDockTailWithStatusColors()
    {
        var state = new ComposerDockState(
            InputText: string.Empty,
            Cursor: 0,
            Prompt: "> ",
            Plan: new PlanDockSummary(
                1,
                3,
                "实现界面",
                "创建测试工具",
                [
                    new PlanDockStep("1", "创建项目", PlanDockStepStatus.Completed),
                    new PlanDockStep("2", "实现界面", PlanDockStepStatus.Running),
                    new PlanDockStep("3", "构建验证", PlanDockStepStatus.Pending),
                ]),
            Agents: new AgentDockSummary(0),
            Model: new ModelDockSummary("deepseek-chat"),
            IsBusy: true);

        var lines = new ComposerDockRenderer().BuildDockLines(
            state,
            popupLines: null,
            styled: true,
            width: 120);

        Assert.DoesNotContain(lines, line => line.Contains("Plan 创建测试工具", StringComparison.Ordinal));
        Assert.Contains(TerminalAnsi.GreenText("  ✓ 1. 创建项目"), lines[^3], StringComparison.Ordinal);
        Assert.Contains(TerminalAnsi.YellowText("  ▶ 2. 实现界面"), lines[^2], StringComparison.Ordinal);
        Assert.Contains(TerminalAnsi.DimText("  □ 3. 构建验证"), lines[^1], StringComparison.Ordinal);
        Assert.Contains(TerminalAnsi.YellowText("• Working..."), lines[2], StringComparison.Ordinal);
        Assert.DoesNotContain("Plan 1/3", lines[2], StringComparison.Ordinal);
    }

    [Fact]
    public void ComposerDockRenderer_BuildsAllPlanStepsAtDockTail()
    {
        var state = new ComposerDockState(
            InputText: string.Empty,
            Cursor: 0,
            Prompt: "> ",
            Plan: new PlanDockSummary(
                5,
                7,
                "验证构建",
                "创建测试工具",
                Enumerable.Range(1, 7)
                    .Select(index => new PlanDockStep(
                        index.ToString(CultureInfo.InvariantCulture),
                        $"步骤 {index}",
                        index <= 5 ? PlanDockStepStatus.Completed : PlanDockStepStatus.Pending))
                    .ToArray()),
            Agents: new AgentDockSummary(0),
            Model: new ModelDockSummary("deepseek-chat"),
            IsBusy: true);

        var lines = new ComposerDockRenderer().BuildDockLines(
            state,
            popupLines: null,
            styled: false,
            width: 120);

        Assert.Contains(lines, line => line.Contains("✓ 5. 步骤 5", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("□ 6. 步骤 6", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("□ 7. 步骤 7", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Contains("还有", StringComparison.Ordinal));
    }

    [Fact]
    public void ComposerDockRenderer_WhenIdleWithResolvedModel_ShowsIdleAndModel()
    {
        var state = new ComposerDockState(
            InputText: string.Empty,
            Cursor: 0,
            Prompt: "> ",
            Plan: new PlanDockSummary(
                3,
                3,
                null,
                "创建测试工具",
                [
                    new PlanDockStep("1", "创建项目", PlanDockStepStatus.Completed),
                    new PlanDockStep("2", "实现界面", PlanDockStepStatus.Completed),
                    new PlanDockStep("3", "构建验证", PlanDockStepStatus.Completed),
                ]),
            Agents: new AgentDockSummary(0),
            Model: new ModelDockSummary("deepseek-chat"),
            IsBusy: false);

        var line = ComposerDockRenderer.BuildStatusLine(state, styled: false);

        Assert.Contains("Idle", line, StringComparison.Ordinal);
        Assert.Contains("Model: deepseek-chat", line, StringComparison.Ordinal);
        Assert.DoesNotContain("Working", line, StringComparison.Ordinal);
        Assert.DoesNotContain("Plan 3/3", line, StringComparison.Ordinal);
    }

    [Fact]
    public void ComposerDockRenderer_WhenModelHasRouteContext_ShowsProviderRouteAndProtocol()
    {
        var state = new ComposerDockState(
            InputText: string.Empty,
            Cursor: 0,
            Prompt: "> ",
            Plan: null,
            Agents: new AgentDockSummary(0),
            Model: new ModelDockSummary(
                "openai-compatible-default",
                "openai-compatible",
                "default",
                "openai_chat_completions"),
            IsBusy: false);

        var line = ComposerDockRenderer.BuildStatusLine(state, styled: false);

        Assert.Contains("Model: openai-compatible / openai-compatible-default", line, StringComparison.Ordinal);
        Assert.Contains("route: default", line, StringComparison.Ordinal);
        Assert.Contains("protocol: openai_chat_completions", line, StringComparison.Ordinal);
    }

    [Fact]
    public void TerminalTranscriptRenderer_WhenToolSummaryContainsSubject_OmitsDuplicateSubjectLine()
    {
        var block = new ToolInvocationBlock(
            ToolPresentationKind.FileChange,
            new ToolInvocationTitle("修改文件"),
            new ToolInvocationSubject(@"C:\Users\Example\Desktop\TestTianShu\HttpTester\MainWindow.xaml"),
            new ToolInvocationResult(@"写入成功：C:\Users\Example\Desktop\TestTianShu\HttpTester\MainWindow.xaml"),
            ToolPresentationStatus.Completed);

        var rendered = new TerminalTranscriptRenderer().Render(block, styled: false);

        Assert.Contains("● 修改文件", rendered, StringComparison.Ordinal);
        Assert.Contains(@"  ✓ 写入成功：C:\Users\Example\Desktop\TestTianShu\HttpTester\MainWindow.xaml", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain(Environment.NewLine + @"  C:\Users\Example\Desktop\TestTianShu\HttpTester\MainWindow.xaml" + Environment.NewLine, rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void TerminalTranscriptRenderer_WhenToolSummaryDoesNotContainSubject_KeepsSubjectLine()
    {
        var block = new ToolInvocationBlock(
            ToolPresentationKind.Command,
            new ToolInvocationTitle("执行命令"),
            new ToolInvocationSubject("Get-Location", AlwaysShow: true),
            new ToolInvocationResult(@"C:\Users\Example"),
            ToolPresentationStatus.Completed);

        var rendered = new TerminalTranscriptRenderer().Render(block, styled: false);

        Assert.Contains("  Get-Location", rendered, StringComparison.Ordinal);
        Assert.Contains(@"  ✓ C:\Users\Example", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void TerminalTranscriptRenderer_WhenCommandSummaryContainsSubject_StillKeepsCommandLine()
    {
        var block = new ToolInvocationBlock(
            ToolPresentationKind.Command,
            new ToolInvocationTitle("执行命令"),
            new ToolInvocationSubject("Get-Location", AlwaysShow: true),
            new ToolInvocationResult("Get-Location -> C:\\Users\\Example"),
            ToolPresentationStatus.Completed);

        var rendered = new TerminalTranscriptRenderer().Render(block, styled: false);

        Assert.Contains("  Get-Location", rendered, StringComparison.Ordinal);
        Assert.Contains("  ✓ Get-Location -> C:\\Users\\Example", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void TerminalMarkdownRenderer_ExpandsDenseMarkdownSummaryIntoReadableLines()
    {
        var rendered = TerminalMarkdownRenderer.RenderLines(
            @"项目已创建并构建成功！**项目位置:** C:\Users\Example\Desktop\TestTianShu\HttpTester\ **文件结构:** HttpTester.csproj",
            styled: false);

        Assert.Contains("项目已创建并构建成功！", rendered);
        Assert.Contains(@"  项目位置: C:\Users\Example\Desktop\TestTianShu\HttpTester\", rendered);
        Assert.Contains("  文件结构: HttpTester.csproj", rendered);
    }

    [Fact]
    public void TerminalMarkdownRenderer_ExpandsInlineHeadingsAndDenseItemsIntoReadableLines()
    {
        var rendered = TerminalMarkdownRenderer.RenderLines(
            @"项目已完成，编译通过（0 警告/0 错误）。##项目结构 `C:\Users\Example\Desktop\TestTianShu\HttpTester` — HttpTester.csproj .NET 10 WPF项目文件 — App.xaml 应用程序入口##功能说明 — URL输入框 — GET/POST方法选择",
            styled: false);

        Assert.Contains("项目已完成，编译通过（0 警告/0 错误）。", rendered);
        Assert.Contains(@"项目结构 C:\Users\Example\Desktop\TestTianShu\HttpTester", rendered);
        Assert.Contains("  • HttpTester.csproj .NET 10 WPF项目文件", rendered);
        Assert.Contains("  • App.xaml 应用程序入口", rendered);
        Assert.Contains("功能说明", rendered);
        Assert.Contains("  • URL输入框", rendered);
        Assert.Contains("  • GET/POST 方法选择", rendered);
    }

    [Fact]
    public void TerminalMarkdownRenderer_PreservesFenceBlockSpacingAndMarkdownLiteralText()
    {
        var rendered = TerminalMarkdownRenderer.RenderLines(
            """
            项目已创建并构建成功！以下是项目概览：

            ## 项目结构

            ```
            C:\Users\Example\Desktop\TestTianShu\
            ├── HttpTester.csproj     # .NET 10 WPF 项目文件
            ├── App.xaml / App.xaml.cs # 应用程序入口
            - **URL 输入框** — 支持回车键快捷发送 GET 请求 — 默认 GET
            ```
            """,
            styled: false);

        Assert.Contains(@"├── HttpTester.csproj     # .NET 10 WPF 项目文件", rendered);
        Assert.Contains(@"├── App.xaml / App.xaml.cs # 应用程序入口", rendered);
        Assert.Contains("- **URL 输入框** — 支持回车键快捷发送 GET 请求 — 默认 GET", rendered);
        Assert.DoesNotContain("  • 支持回车键快捷发送 GET 请求", rendered);
    }

    [Fact]
    public void TerminalMarkdownRenderer_RendersMarkdownTableAsReadableTerminalRows()
    {
        var rendered = TerminalMarkdownRenderer.RenderLines(
            """
            ### 功能概览

            | 区域 | 说明 |
            |------|------|
            | **HTTP 方法** | 下拉框支持 GET / POST / PUT / DELETE |
            | **URL** | 默认填入 `https://httpbin.org/get` |
            | **请求头** | 可编辑，每行一个 `Key: Value` |

            在项目目录中执行：

            ```powershell
            cd C:\Users\Example\Desktop\TestTianShu\HttpTester
            dotnet run
            ```
            """,
            styled: true).Select(StripAnsi).ToList();

        Assert.Contains("功能概览", rendered);
        Assert.Contains("  HTTP 方法：下拉框支持 GET / POST / PUT / DELETE", rendered);
        Assert.Contains("  URL：默认填入 https://httpbin.org/get", rendered);
        Assert.Contains("  请求头：可编辑，每行一个 Key: Value", rendered);
        Assert.Contains(@"cd C:\Users\Example\Desktop\TestTianShu\HttpTester", rendered);
        Assert.Contains("dotnet run", rendered);
        Assert.DoesNotContain(rendered, line => line.Contains("|------|", StringComparison.Ordinal));
    }

    [Fact]
    public void TerminalMarkdownRenderer_MapsMarkdownAstBlocksToTerminalLines()
    {
        var rendered = TerminalMarkdownRenderer.RenderLines(
            """
            ## 完成摘要

            - 创建 WPF 项目
            - 运行 `dotnet build`

            | 项目 | 结果 |
            |---|---|
            | 构建 | 通过 |

            ```powershell
            dotnet new wpf
            dotnet build
            ```
            """,
            styled: true).Select(StripAnsi).ToList();

        Assert.Contains("完成摘要", rendered);
        Assert.Contains("• 创建 WPF 项目", rendered);
        Assert.Contains("• 运行 dotnet build", rendered);
        Assert.Contains("  构建：通过", rendered);
        Assert.Contains("dotnet new wpf", rendered);
        Assert.Contains("dotnet build", rendered);
        Assert.DoesNotContain(rendered, line => line.Contains("|---|", StringComparison.Ordinal));
    }

    [Fact]
    public void TerminalMarkdownRenderer_DoesNotSplitUrlSchemeAsDenseLabel()
    {
        var rendered = TerminalMarkdownRenderer.RenderLines(
            "- **URL输入框** — 默认填入 `https://httpbin.org/get` 方便测试",
            styled: false);

        Assert.Contains("• URL输入框 — 默认填入 https://httpbin.org/get 方便测试", rendered);
        Assert.DoesNotContain(rendered, line => line.TrimStart().StartsWith("默认填入 https:", StringComparison.Ordinal));
        Assert.DoesNotContain(rendered, line => line.Contains("https: //", StringComparison.Ordinal));
    }

    [Fact]
    public void TerminalFrameBuilder_RendersUserMessageBlockAsDistinctBlueSeparatorBlock()
    {
        var blocks = new ChatPresentationBlock[]
        {
            new UserMessageBlock("如果让你优化这个项目，你要怎么优化？"),
        };
        var frame = new TerminalFrameBuilder().Build(
            new ChatPresentationState(
                blocks,
                ActiveAssistantText: string.Empty,
                Plan: null,
                Output: ConversationOutputModel.FromBlocks(blocks)),
            new TerminalFrameBuildContext(
                InputText: string.Empty,
                Cursor: 0,
                Prompt: "> ",
                Agents: null,
                Model: null,
                IsBusy: false,
                WorkingElapsed: null,
                QueuedFollowUps: null,
                InputNotice: null,
                CommandOverlay: null,
                PopupLines: null,
                Width: 120,
                Styled: true));

        var lines = frame.TranscriptLines.Select(StripAnsi).ToArray();
        var expectedSeparator = new string('-', 120);
        Assert.Equal(string.Empty, lines[0]);
        Assert.Contains("用户消息", lines[1], StringComparison.Ordinal);
        Assert.Equal("如果让你优化这个项目，你要怎么优化？", lines[2]);
        Assert.Equal(expectedSeparator, lines[3]);
        Assert.Equal(string.Empty, lines[4]);
        Assert.Contains(TerminalAnsi.SkyBlueText(" 用户消息 "), frame.TranscriptLines[1], StringComparison.Ordinal);
        Assert.Contains(TerminalAnsi.SkyBlueText("如果让你优化这个项目，你要怎么优化？"), frame.TranscriptLines[2], StringComparison.Ordinal);
        Assert.Contains(TerminalAnsi.SkyBlueText(expectedSeparator), frame.TranscriptLines[3], StringComparison.Ordinal);
        Assert.DoesNotContain(TerminalAnsi.BlueText("如果让你优化这个项目，你要怎么优化？"), frame.TranscriptLines[2], StringComparison.Ordinal);
        Assert.DoesNotContain("用户消息", lines[3], StringComparison.Ordinal);
        Assert.Equal(120, TerminalLayoutCalculator.GetDisplayWidth(lines[1]));
    }

    [Fact]
    public void TerminalFrameBuilder_RendersGuidanceUserMessageBlockWithColoredLabels()
    {
        var blocks = new ChatPresentationBlock[]
        {
            new UserMessageBlock("待发送0", "正在引导"),
            new UserMessageBlock("待发送1", "引导成功"),
        };
        var frame = new TerminalFrameBuilder().Build(
            new ChatPresentationState(
                blocks,
                ActiveAssistantText: string.Empty,
                Plan: null,
                Output: ConversationOutputModel.FromBlocks(blocks)),
            new TerminalFrameBuildContext(
                InputText: string.Empty,
                Cursor: 0,
                Prompt: "> ",
                Agents: null,
                Model: null,
                IsBusy: false,
                WorkingElapsed: null,
                QueuedFollowUps: null,
                InputNotice: null,
                CommandOverlay: null,
                PopupLines: null,
                Width: 40,
                Styled: true));

        var lines = frame.TranscriptLines.Select(StripAnsi).ToArray();

        Assert.Equal(string.Empty, lines[0]);
        Assert.Contains("正在引导", lines[1], StringComparison.Ordinal);
        Assert.Equal("待发送0", lines[2]);
        Assert.Equal(new string('-', 40), lines[3]);
        Assert.Equal(string.Empty, lines[4]);
        Assert.Equal(string.Empty, lines[5]);
        Assert.Contains("引导成功", lines[6], StringComparison.Ordinal);
        Assert.Equal("待发送1", lines[7]);
        Assert.Equal(new string('-', 40), lines[8]);
        Assert.Contains(TerminalAnsi.YellowText(" 正在引导 "), frame.TranscriptLines[1], StringComparison.Ordinal);
        Assert.Contains(TerminalAnsi.GreenText(" 引导成功 "), frame.TranscriptLines[6], StringComparison.Ordinal);
    }

    [Fact]
    public void TerminalMarkdownRenderer_ExpandsDenseColonHyphenSummaryItems()
    {
        var rendered = TerminalMarkdownRenderer.RenderLines(
            "已完成！项目位置：`C:\\Users\\Example\\Desktop\\TestTianShu\\HttpTester`文件结构：-MainWindow.xaml-界面布局-MainWindow.xaml.cs-GET/POST请求逻辑-HttpTester.csproj-项目文件功能说明：-顶部可切换GET/POST方法-响应区域展示HTTP状态码和返回内容",
            styled: true).Select(StripAnsi).ToList();

        Assert.Contains(@"  项目位置： C:\Users\Example\Desktop\TestTianShu\HttpTester", rendered);
        Assert.Contains("  文件结构：", rendered);
        Assert.Contains("  • MainWindow.xaml", rendered);
        Assert.Contains("  • 界面布局", rendered);
        Assert.Contains("  • MainWindow.xaml.cs", rendered);
        Assert.Contains("  功能说明：", rendered);
        Assert.Contains("  • 顶部可切换 GET/POST 方法", rendered);
        Assert.Contains("  • 响应区域展示 HTTP 状态码和返回内容", rendered);
    }

    [Fact]
    public void TerminalMarkdownRenderer_NormalizesCompactTechTokenSpacing()
    {
        var rendered = TerminalMarkdownRenderer.RenderLines(
            "先检查一下可用的 .NETSDK版本。检测到.NET10.0SDK已安装，我来创建完整的WPFHTTP测试工具，并支持GET/POST请求。",
            styled: false);

        var line = Assert.Single(rendered);
        Assert.Contains(".NET SDK 版本", line, StringComparison.Ordinal);
        Assert.Contains(".NET 10.0 SDK 已安装", line, StringComparison.Ordinal);
        Assert.Contains("WPF HTTP 测试工具", line, StringComparison.Ordinal);
        Assert.Contains("GET/POST 请求", line, StringComparison.Ordinal);
    }

    [Fact]
    public void TerminalMarkdownRenderer_PreservesInlineCodeSpaces()
    {
        var rendered = TerminalMarkdownRenderer.RenderLines(
            "先使用 `dotnet new wpf` 创建项目，再执行 `dotnet run`。",
            styled: true);

        Assert.Contains(
            "先使用 dotnet new wpf 创建项目，再执行 dotnet run。",
            StripAnsi(Assert.Single(rendered)),
            StringComparison.Ordinal);
    }

    [Fact]
    public void TerminalMarkdownRenderer_PreservesSpacesBetweenAdjacentInlineCodeCommands()
    {
        var rendered = TerminalMarkdownRenderer.RenderLines(
            @"运行方式： `powershell` `cd C:\Users\Example\Desktop\TestTianShu\HttpTester` `dotnet run` 或用 `dotnet build` 编译。",
            styled: true);

        var line = StripAnsi(Assert.Single(rendered));
        Assert.Contains(
            @"运行方式： powershell cd C:\Users\Example\Desktop\TestTianShu\HttpTester dotnet run 或用 dotnet build 编译。",
            line,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TerminalMarkdownRenderer_PreservesSpacesWhenInlineFenceContainsCommandLines()
    {
        var rendered = TerminalMarkdownRenderer.RenderLines(
            """
            运行方式： ```powershell
            cd C:\Users\Example\Desktop\TestTianShu\HttpTester
            dotnet run
            ```或用 `dotnet build` 编译后运行。
            """,
            styled: true).Select(StripAnsi).ToList();

        Assert.Contains("运行方式： powershell", rendered[0], StringComparison.Ordinal);
        Assert.Contains(@"cd C:\Users\Example\Desktop\TestTianShu\HttpTester", string.Join(" ", rendered), StringComparison.Ordinal);
        Assert.Contains("dotnet run", string.Join(" ", rendered), StringComparison.Ordinal);
        Assert.DoesNotContain("powershellcd", string.Join(" ", rendered), StringComparison.Ordinal);
        Assert.DoesNotContain("dotnetrun", string.Join(" ", rendered), StringComparison.Ordinal);
        Assert.Contains("或用 dotnet build 编译后运行。", string.Join(" ", rendered), StringComparison.Ordinal);
    }

    [Fact]
    public void ComposerDockRenderer_StyledStatusLineKeepsWorkingVisible()
    {
        var state = new ComposerDockState(
            InputText: string.Empty,
            Cursor: 0,
            Prompt: "> ",
            Plan: new PlanDockSummary(0, 2, "生成界面"),
            Agents: new AgentDockSummary(0),
            Model: new ModelDockSummary("deepseek-chat"),
            IsBusy: true);

        var line = ComposerDockRenderer.BuildStatusLine(state, styled: true);

        Assert.Contains(TianShu.Cli.Terminal.TerminalAnsi.YellowText("• Working..."), line, StringComparison.Ordinal);
        Assert.Contains(TianShu.Cli.Terminal.TerminalAnsi.DimText("Plan 0/2"), line, StringComparison.Ordinal);
        Assert.False(line.StartsWith(TianShu.Cli.Terminal.TerminalAnsi.Dim, StringComparison.Ordinal));
    }

    private static string StripAnsi(string value)
        => AnsiRegex().Replace(value, string.Empty);

    [GeneratedRegex(@"\u001b\[[0-9; ]*[A-Za-z]")]
    private static partial Regex AnsiRegex();

    [Fact]
    public void ComposerDockRenderer_WorkingStatusIncludesCompactElapsedTime()
    {
        var state = new ComposerDockState(
            InputText: string.Empty,
            Cursor: 0,
            Prompt: "> ",
            Plan: null,
            Agents: new AgentDockSummary(0),
            Model: new ModelDockSummary("deepseek-chat"),
            IsBusy: true,
            WorkingElapsed: TimeSpan.FromSeconds((2 * 24 * 60 * 60) + (3 * 60 * 60) + (20 * 60) + 30));

        var line = ComposerDockRenderer.BuildStatusLine(state, styled: false);

        Assert.Contains("• Working... 2d 3h 20m 30s", line, StringComparison.Ordinal);
        Assert.DoesNotContain("0h", line, StringComparison.Ordinal);
    }
}
