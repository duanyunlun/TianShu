using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using TianShu.Contracts.Agents;
using TianShu.Contracts.Artifacts;
using TianShu.Contracts.Catalog;
using TianShu.Contracts.Collaboration;
using TianShu.Contracts.Conversations;
using TianShu.Contracts.Execution;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Sessions;
using TianShu.Contracts.Tools;
using TianShu.Contracts.Workflows;
using TianShu.AppHost.Configuration;
using TianShu.Cli.Interaction.Rendering;
using TianShu.Cli.Terminal;
using TianShu.Execution.Runtime;
using TianShu.Execution.Runtime.Events;
using TianShu.RuntimeComposition;
using Task = System.Threading.Tasks.Task;

namespace TianShu.Cli.Tests;

public sealed class TianShuCliExpansionTests
{
    private static readonly Assembly CliAssembly = ReflectionTestHelper.LoadRequiredAssembly("TianShu.Cli");

    [Fact]
    public void CliHelpText_CommandDescriptions_DoNotPresentFormalSurfacesAsLegacyCompatibility()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var helpText = Assert.IsType<string>(ReflectionTestHelper.InvokeStaticMethod(parserType, "GetHelpText"));

        Assert.Contains("review      代码审查能力：uncommitted / base / commit / start", helpText, StringComparison.Ordinal);
        Assert.Contains("code-mode   代码执行能力：exec / wait", helpText, StringComparison.Ordinal);
        Assert.Contains("git-diff    获取线程对应的远端 git diff", helpText, StringComparison.Ordinal);
        Assert.DoesNotContain("顶层兼容 exec review", helpText, StringComparison.Ordinal);
        Assert.DoesNotContain("旧 code_mode typed 能力", helpText, StringComparison.Ordinal);
        Assert.DoesNotContain("兼容内核旧接口", helpText, StringComparison.Ordinal);
    }

    [Fact]
    public void InteractiveChatRunner_PendingInteractiveRequests_UseDedicatedStore()
    {
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.Interaction.Host.InteractiveChatSessionHost");
        var storeType = ReflectionTestHelper.GetRequiredType(
            CliAssembly,
            "TianShu.Cli.Interaction.Orchestration.PendingInteractiveRequestStore");

        var pendingField = runnerType.GetField("pendingInteractiveRequests", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(pendingField);
        Assert.Equal(storeType, pendingField!.FieldType);
    }

    [Fact]
    public void InteractiveChatRunner_ShouldUseTerminalChatTui_OnlyForHumanInteractiveTerminal()
    {
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.InteractiveChatRunner");
        var options = CreateChatOptions();

        Assert.True(ShouldUseTerminalChatTui(runnerType, options, hasScript: false, isInputRedirected: false, isOutputRedirected: false));
        Assert.False(ShouldUseTerminalChatTui(runnerType, options, hasScript: true, isInputRedirected: false, isOutputRedirected: false));
        Assert.False(ShouldUseTerminalChatTui(runnerType, options, hasScript: false, isInputRedirected: true, isOutputRedirected: false));
        Assert.False(ShouldUseTerminalChatTui(runnerType, options, hasScript: false, isInputRedirected: false, isOutputRedirected: true));

        var jsonlOptions = CreateChatOptions();
        var protocolType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.ChatOutputProtocol");
        ReflectionTestHelper.SetProperty(jsonlOptions, "OutputProtocol", Enum.Parse(protocolType, "Jsonl"));

        Assert.False(ShouldUseTerminalChatTui(runnerType, jsonlOptions, hasScript: false, isInputRedirected: false, isOutputRedirected: false));
    }

    [Fact]
    public void InteractiveChatRunner_RunAsync_UsesNativeConsolePromptForMainChatInput()
    {
        var sourcePath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Host",
            "InteractiveChatSessionHost.cs");
        var source = File.ReadAllText(sourcePath);
        var runAsyncStart = source.IndexOf("public async Task<int> RunAsync", StringComparison.Ordinal);
        var terminalChatStart = source.IndexOf("private Task<bool> RunTianShuTerminalChatAsync", runAsyncStart, StringComparison.Ordinal);

        Assert.True(runAsyncStart >= 0);
        Assert.True(terminalChatStart > runAsyncStart);

        var runAsyncSource = source[runAsyncStart..terminalChatStart];

        Assert.DoesNotContain("TerminalGuiChatSurface.Create", runAsyncSource, StringComparison.Ordinal);
        Assert.DoesNotContain("RunTerminalChatTuiAsync(", runAsyncSource, StringComparison.Ordinal);
        Assert.Contains("ResolveCurrentModelForDock(options)", runAsyncSource, StringComparison.Ordinal);
        Assert.Contains("WriteNativeTerminalStartupBanner(options)", runAsyncSource, StringComparison.Ordinal);
        Assert.Contains("RunTianShuTerminalChatAsync(", runAsyncSource, StringComparison.Ordinal);
        Assert.DoesNotContain("TerminalGuiChatSurface.Create", runAsyncSource, StringComparison.Ordinal);
    }

    [Fact]
    public void InteractiveChatRunner_StartupBanner_DoesNotPrintStandaloneTip()
    {
        var sourcePath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Host",
            "InteractiveChatSessionHost.cs");
        var source = File.ReadAllText(sourcePath);
        var methodStart = source.IndexOf("private void WriteNativeTerminalStartupBanner", StringComparison.Ordinal);
        var nextMethodStart = source.IndexOf("private async Task<bool> ExecuteSlashCommandAsync", methodStart, StringComparison.Ordinal);

        Assert.True(methodStart >= 0);
        Assert.True(nextMethodStart > methodStart);

        var methodSource = source[methodStart..nextMethodStart];

        Assert.Contains("WriteDisplayLine(plainBanner, styledBanner)", methodSource, StringComparison.Ordinal);
        Assert.DoesNotContain("TerminalStartupBanner.BuildTip", methodSource, StringComparison.Ordinal);
    }

    [Fact]
    public void InteractiveChatRunner_TerminalChatLoop_UsesDedicatedInputLoopHost()
    {
        var sourcePath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Host",
            "InteractiveChatSessionHost.cs");
        var source = File.ReadAllText(sourcePath);
        var terminalInputSourcePath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Host",
            "InteractiveChatSessionHost.TerminalInput.cs");
        var terminalInputSource = File.ReadAllText(terminalInputSourcePath);
        var terminalChatStart = source.IndexOf("private Task<bool> RunTianShuTerminalChatAsync", StringComparison.Ordinal);
        var nextMethodStart = source.IndexOf("private static bool ShouldShowTerminalWaitingPlaceholder", terminalChatStart, StringComparison.Ordinal);

        Assert.True(terminalChatStart >= 0);
        Assert.True(nextMethodStart > terminalChatStart);

        var terminalChatSource = source[terminalChatStart..nextMethodStart];
        var loopSourcePath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Host",
            "TerminalChatInputLoop.cs");
        var loopContextSourcePath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Host",
            "TerminalChatInputLoopContext.cs");
        var loopResultSourcePath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Host",
            "TerminalChatInputLoopResult.cs");
        var loopSource = File.ReadAllText(loopSourcePath);
        var loopContextSource = File.ReadAllText(loopContextSourcePath);
        var loopResultSource = File.ReadAllText(loopResultSourcePath);

        Assert.Contains("RunTianShuTerminalChatCoreAsync(", terminalChatSource, StringComparison.Ordinal);
        Assert.Contains("terminalInputLoop.RunAsync(", terminalInputSource, StringComparison.Ordinal);
        Assert.Contains("TerminalSubmitIntent.Queue => ControlPlaneFollowUpMode.Queue", terminalInputSource, StringComparison.Ordinal);
        Assert.Contains("TerminalSubmitIntent.Steer => ControlPlaneFollowUpMode.Steer", terminalInputSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new TerminalChatComposer", terminalChatSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new TerminalPromptRenderer", terminalChatSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new TerminalSuggestionPopup", terminalChatSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ConsoleTerminalInput.Shared.ReadKeyAsync", terminalChatSource, StringComparison.Ordinal);
        Assert.Contains("new TerminalChatComposer", loopSource, StringComparison.Ordinal);
        Assert.Contains("new TerminalPromptRenderer", loopSource, StringComparison.Ordinal);
        Assert.Contains("new TerminalSuggestionPopup(context.Options.WorkingDirectory)", loopSource, StringComparison.Ordinal);
        Assert.Contains("ConsoleTerminalInput.Shared.ReadKeyAsync", loopSource, StringComparison.Ordinal);
        Assert.Contains("selectedSuggestion.InsertText", loopSource, StringComparison.Ordinal);
        Assert.DoesNotContain("internal sealed record TerminalChatInputLoopContext", loopSource, StringComparison.Ordinal);
        Assert.DoesNotContain("internal readonly record struct TerminalChatInputLoopResult", loopSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed record TerminalChatInputLoopContext", loopContextSource, StringComparison.Ordinal);
        Assert.Contains("SubmitLineAsync", loopContextSource, StringComparison.Ordinal);
        Assert.Contains("internal readonly record struct TerminalChatInputLoopResult", loopResultSource, StringComparison.Ordinal);
        Assert.Contains("ExitRequested", loopResultSource, StringComparison.Ordinal);
        Assert.DoesNotContain("WaitForIdleAsync(runtime", terminalChatSource, StringComparison.Ordinal);
        Assert.DoesNotContain("fixedComposerDockController", terminalChatSource, StringComparison.Ordinal);
    }

    [Fact]
    public void InteractiveChatSessionHost_SlashCommands_UseBufferedScopeForWholeCommand()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Host",
            "InteractiveChatSessionHost.cs"));
        var start = source.IndexOf("private async Task<bool> ExecuteSlashCommandAsync", StringComparison.Ordinal);
        var end = source.IndexOf("private ChatSlashCommandContext CreateSlashCommandContext", start, StringComparison.Ordinal);

        Assert.True(start >= 0);
        Assert.True(end > start);

        var methodSource = source[start..end];
        Assert.Contains("BeginControlOutputScope(buffered: true, queueExternalOutput: false)", methodSource, StringComparison.Ordinal);
    }

    [Fact]
    public void InteractiveChatSessionHost_PlainThreadLifecycleCommands_UseBufferedScopeForWholeCommand()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Host",
            "InteractiveChatSessionHost.cs"));
        var start = source.IndexOf("private async Task<bool> TryExecutePlainThreadLifecycleInputAsync", StringComparison.Ordinal);
        var end = source.IndexOf("private Task<bool> TryExecuteShellInputAsync", start, StringComparison.Ordinal);

        Assert.True(start >= 0);
        Assert.True(end > start);

        var methodSource = source[start..end];
        Assert.Contains("BeginControlOutputScope(buffered: true, queueExternalOutput: false)", methodSource, StringComparison.Ordinal);
    }

    [Fact]
    public void InteractiveChatRunner_FinalTypedFirstBoundary_DoesNotOwnParsingRenderingOrInputLoopDetails()
    {
        var runnerSourcePath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Host",
            "InteractiveChatSessionHost.cs");
        var runnerSource = File.ReadAllText(runnerSourcePath);
        var lineCount = File.ReadLines(runnerSourcePath).Count();

        Assert.InRange(lineCount, 1, 1750);
        Assert.DoesNotContain("new TerminalChatComposer", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ConsoleTerminalInput", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonDocument.Parse", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonSerializer.Serialize", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("currentTerminalPromptFrame", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("assistantRetainedTail", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("waitingTimer", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("workingDockTimer", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("switch (classified.Kind)", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("case SlashCommandKind", runnerSource, StringComparison.Ordinal);
    }

    [Fact]
    public void InteractiveChatRunner_FinalShape_IsThinFacadeOverSessionHostFactory()
    {
        var repoRoot = FindRepositoryRoot();
        var runnerSourcePath = Path.Combine(
            repoRoot,
            "src",
            "Presentations",
            "TianShu.Cli",
            "InteractiveChatRunner.cs");
        var sessionHostSourcePath = Path.Combine(
            repoRoot,
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Host",
            "InteractiveChatSessionHost.cs");
        var hostFactorySourcePath = Path.Combine(
            repoRoot,
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Host",
            "InteractiveChatHostFactory.cs");

        var runnerSource = File.ReadAllText(runnerSourcePath);
        var sessionHostSource = File.ReadAllText(sessionHostSourcePath);
        var hostFactorySource = File.ReadAllText(hostFactorySourcePath);
        var runnerLineCount = File.ReadLines(runnerSourcePath).Count();

        Assert.InRange(runnerLineCount, 1, 80);
        Assert.Contains("private readonly IInteractiveChatHostFactory hostFactory", runnerSource, StringComparison.Ordinal);
        Assert.Contains("hostFactory.Create().RunAsync(options, cancellationToken)", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("TerminalInteractionHost", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ChatOutputWriter", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ChatSlashCommandDispatcher", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("PendingInteractiveRequestStore", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonSerializerOptions", runnerSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed partial class InteractiveChatSessionHost", sessionHostSource, StringComparison.Ordinal);
        Assert.Contains("internal interface IInteractiveChatHostFactory", hostFactorySource, StringComparison.Ordinal);
        Assert.Contains("InteractiveChatSessionHost Create()", hostFactorySource, StringComparison.Ordinal);
    }

    [Fact]
    public void InteractiveChatRunner_UsesInlineTailPromptInsteadOfFixedViewportDock()
    {
        var runnerSourcePath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Host",
            "InteractiveChatSessionHost.cs");
        var hostSourcePath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Host",
            "TerminalInteractionHost.cs");
        var tailControllerSourcePath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Host",
            "AssistantRetainedTailController.cs");
        var runnerSource = File.ReadAllText(runnerSourcePath);
        var hostSource = File.ReadAllText(hostSourcePath);
        var tailControllerSource = File.ReadAllText(tailControllerSourcePath);

        Assert.DoesNotContain("FixedComposerDockController", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("fixedComposerDockController", runnerSource, StringComparison.Ordinal);
        Assert.Contains("ChatOutputWriter", runnerSource, StringComparison.Ordinal);
        Assert.Contains("terminalHost.RefreshAndRestoreInlineTailPrompt()", runnerSource, StringComparison.Ordinal);
        Assert.Contains("WriteProjectionCommittedBlocks(", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("currentAssistantBuffer", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("currentTerminalPromptFrame", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("currentComposerDockState", runnerSource, StringComparison.Ordinal);

        Assert.Contains("terminalChatFrameRenderer.RenderTranscriptLines(frame)", hostSource, StringComparison.Ordinal);
        Assert.Contains("terminalChatFrameRenderer.RenderTranscriptLines(frame)", hostSource, StringComparison.Ordinal);
        Assert.Contains("assistantTail.RenderUnsafe()", hostSource, StringComparison.Ordinal);
        Assert.Contains("assistantTail.CommitUnsafe()", hostSource, StringComparison.Ordinal);
        Assert.Contains("getPipeline().PresentationState.ActiveAssistantText", hostSource, StringComparison.Ordinal);
        Assert.DoesNotContain("assistantRetainedTailCursorLineIndex", hostSource, StringComparison.Ordinal);
        Assert.DoesNotContain("MoveCursorToAssistantRetainedDockInputUnsafe", hostSource, StringComparison.Ordinal);
        Assert.Contains("frameRenderer.RenderLines(frame, styled: true)", tailControllerSource, StringComparison.Ordinal);
        Assert.Contains("ReadUncommittedAssistantRetainedText", hostSource, StringComparison.Ordinal);
        Assert.Contains("TerminalLayoutCalculator.SafeWritableWidth()", hostSource, StringComparison.Ordinal);
        Assert.Contains("MoveCursorToDockInputUnsafe", tailControllerSource, StringComparison.Ordinal);
        Assert.Contains("ComposerDockRenderer.CalculateCursorColumn(dockState)", tailControllerSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ComposerDock_ViewModels_AreSplitByUiRegion()
    {
        var root = FindRepositoryRoot();
        var renderingRoot = Path.Combine(
            root,
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Rendering");
        var dockSource = File.ReadAllText(Path.Combine(renderingRoot, "ComposerDockState.cs"));
        var inputSource = File.ReadAllText(Path.Combine(renderingRoot, "ComposerInputState.cs"));
        var suggestionSource = File.ReadAllText(Path.Combine(renderingRoot, "SuggestionPopupState.cs"));
        var statusSource = File.ReadAllText(Path.Combine(renderingRoot, "StatusBarState.cs"));
        var planSource = File.ReadAllText(Path.Combine(renderingRoot, "PlanDockModels.cs"));
        var agentSource = File.ReadAllText(Path.Combine(renderingRoot, "AgentDockSummary.cs"));
        var modelSource = File.ReadAllText(Path.Combine(renderingRoot, "ModelDockSummary.cs"));

        Assert.Contains("internal sealed record ComposerDockState", dockSource, StringComparison.Ordinal);
        Assert.Contains("public ComposerInputState Input", dockSource, StringComparison.Ordinal);
        Assert.Contains("public StatusBarState StatusBar", dockSource, StringComparison.Ordinal);
        Assert.Contains("public PlanDockState? PlanPanel", dockSource, StringComparison.Ordinal);
        Assert.DoesNotContain("internal sealed record ComposerInputState", dockSource, StringComparison.Ordinal);
        Assert.DoesNotContain("internal sealed record StatusBarState", dockSource, StringComparison.Ordinal);
        Assert.DoesNotContain("internal sealed record PlanDockSummary", dockSource, StringComparison.Ordinal);

        Assert.Contains("internal sealed record ComposerInputState", inputSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed record SuggestionPopupState", suggestionSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed record StatusBarState", statusSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed record PlanDockSummary", planSource, StringComparison.Ordinal);
        Assert.Contains("internal enum PlanDockStepStatus", planSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed record AgentDockSummary", agentSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed record ModelDockSummary", modelSource, StringComparison.Ordinal);
    }

    [Fact]
    public void InteractiveChatRunner_ChatOutputWriting_UsesDedicatedWriterFacade()
    {
        var root = FindRepositoryRoot();
        var runnerSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Host",
            "InteractiveChatSessionHost.cs"));
        var writerSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Host",
            "ChatOutputWriter.cs"));
        var jsonlWriterSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Host",
            "ChatJsonlOutputWriter.cs"));
        var terminalHostSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Host",
            "IChatOutputTerminalHost.cs"));
        var writerContextSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Host",
            "ChatOutputWriterContext.cs"));
        var frameWriteCoordinatorSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Host",
            "TerminalFrameWriteCoordinator.cs"));

        Assert.Contains("ChatOutputWriter", runnerSource, StringComparison.Ordinal);
        Assert.Contains("chatOutputWriter.WriteProjectionCommittedBlocks", runnerSource, StringComparison.Ordinal);
        Assert.Contains("chatOutputWriter.WriteLine", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Console.WriteLine(JsonSerializer.Serialize(new", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("partial = true", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("partial = false", runnerSource, StringComparison.Ordinal);

        Assert.DoesNotContain("JsonSerializer.Serialize", writerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Console.WriteLine(JsonSerializer.Serialize", writerSource, StringComparison.Ordinal);
        Assert.Contains("ChatJsonlOutputWriter.WriteStdout", writerSource, StringComparison.Ordinal);
        Assert.Contains("ChatJsonlOutputWriter.Write(", writerSource, StringComparison.Ordinal);
        Assert.Contains("TerminalFrameWriteCoordinator", writerSource, StringComparison.Ordinal);
        Assert.Contains("terminalFrameWriter.WritePresentationBlock", writerSource, StringComparison.Ordinal);
        Assert.Contains("terminalFrameWriter.WriteRetainedText", writerSource, StringComparison.Ordinal);
        Assert.Contains("BeginExclusiveTerminalFrameScope", writerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("internal interface IChatOutputTerminalHost", writerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("internal sealed class ChatOutputWriterContext", writerSource, StringComparison.Ordinal);

        Assert.Contains("internal sealed class TerminalFrameWriteCoordinator", frameWriteCoordinatorSource, StringComparison.Ordinal);
        Assert.Contains("WriteHumanTerminalPresentationBlock", frameWriteCoordinatorSource, StringComparison.Ordinal);
        Assert.Contains("WriteHumanTerminalRetainedText", frameWriteCoordinatorSource, StringComparison.Ordinal);
        Assert.Contains("PrepareInlineTailPromptWrite", frameWriteCoordinatorSource, StringComparison.Ordinal);
        Assert.Contains("BeginExclusiveFrameScope", frameWriteCoordinatorSource, StringComparison.Ordinal);
        Assert.Contains("RestoreInlineTailPrompt", frameWriteCoordinatorSource, StringComparison.Ordinal);

        Assert.Contains("ChatJsonlOutputWriter", jsonlWriterSource, StringComparison.Ordinal);
        Assert.Contains("JsonSerializer.Serialize", jsonlWriterSource, StringComparison.Ordinal);
        Assert.Contains("JsonPropertyName(\"type\")", jsonlWriterSource, StringComparison.Ordinal);
        Assert.Contains("JsonPropertyName(\"text\")", jsonlWriterSource, StringComparison.Ordinal);
        Assert.Contains("JsonPropertyName(\"partial\")", jsonlWriterSource, StringComparison.Ordinal);

        Assert.Contains("internal interface IChatOutputTerminalHost", terminalHostSource, StringComparison.Ordinal);
        Assert.Contains("WriteHumanTerminalPresentationBlock", terminalHostSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class ChatOutputWriterContext", writerContextSource, StringComparison.Ordinal);
        Assert.Contains("Func<ChatOutputProtocol> GetOutputProtocol", writerContextSource, StringComparison.Ordinal);
    }

    [Fact]
    public void InteractiveChatRunner_SessionState_UsesDedicatedStateFacade()
    {
        var root = FindRepositoryRoot();
        var runnerSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Host",
            "InteractiveChatSessionHost.cs"));
        var stateSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Orchestration",
            "ChatSessionState.cs"));

        Assert.Contains("ChatSessionState", runnerSource, StringComparison.Ordinal);
        Assert.Contains("SessionState = sessionState", runnerSource, StringComparison.Ordinal);
        Assert.Contains("sessionState.CurrentThreadId", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("lastObservedThreadId", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("lastObservedTurnId", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("lastObservedTurnStatus", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("sessionActiveThreadId", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("expectedInterruptedTurns", runnerSource, StringComparison.Ordinal);

        Assert.Contains("LastObservedThreadId", stateSource, StringComparison.Ordinal);
        Assert.Contains("LastObservedTurnId", stateSource, StringComparison.Ordinal);
        Assert.Contains("LastObservedTurnStatus", stateSource, StringComparison.Ordinal);
        Assert.Contains("SessionActiveThreadId", stateSource, StringComparison.Ordinal);
        Assert.Contains("expectedInterruptedTurns", stateSource, StringComparison.Ordinal);
        Assert.Contains("TryConsumeUserRequestedInterrupt", stateSource, StringComparison.Ordinal);
    }

    [Fact]
    public void InteractiveChatRunner_PendingInteractiveRequests_UseDedicatedStoreFacade()
    {
        var root = FindRepositoryRoot();
        var runnerSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Host",
            "InteractiveChatSessionHost.cs"));
        var storeSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Orchestration",
            "PendingInteractiveRequestStore.cs"));

        Assert.Contains("PendingInteractiveRequestStore", runnerSource, StringComparison.Ordinal);
        Assert.Contains("PendingInteractiveRequests = pendingInteractiveRequests", runnerSource, StringComparison.Ordinal);
        Assert.Contains("pendingInteractiveRequests.Clear(", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("pendingApprovals", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("pendingPermissionRequests", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("pendingUserInputs", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("MatchesPendingInteractiveTurn", runnerSource, StringComparison.Ordinal);

        Assert.Contains("ConcurrentDictionary<string, CliPendingApprovalRequestState>", storeSource, StringComparison.Ordinal);
        Assert.Contains("ClearForTurn", storeSource, StringComparison.Ordinal);
        Assert.Contains("MatchesTurn", storeSource, StringComparison.Ordinal);
    }

    [Fact]
    public void InteractiveChatRunner_PendingInteractiveAutomation_UsesDedicatedHandler()
    {
        var root = FindRepositoryRoot();
        var runnerSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Host",
            "InteractiveChatSessionHost.cs"));
        var handlerSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Orchestration",
            "PendingInteractiveAutomationHandler.cs"));

        Assert.Contains("PendingInteractiveAutomationHandler", runnerSource, StringComparison.Ordinal);
        Assert.Contains("PendingInteractiveAutomation = pendingInteractiveAutomation", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("CliApprovalResponseResolver.BuildResolution", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("permissionScript.TryResolveResponse", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("userInputScript.TryResolveAnswers", runnerSource, StringComparison.Ordinal);

        Assert.Contains("CliApprovalResponseResolver.BuildResolution", handlerSource, StringComparison.Ordinal);
        Assert.Contains("permissionScript.TryResolveResponse", handlerSource, StringComparison.Ordinal);
        Assert.Contains("userInputScript.TryResolveAnswers", handlerSource, StringComparison.Ordinal);
    }

    [Fact]
    public void InteractiveChatRunner_StreamEvents_UseDedicatedConsumer()
    {
        var root = FindRepositoryRoot();
        var runnerSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Host",
            "InteractiveChatSessionHost.cs"));
        var consumerSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Orchestration",
            "ChatStreamEventConsumer.cs"));

        Assert.Contains("ChatStreamEventConsumer", runnerSource, StringComparison.Ordinal);
        Assert.Contains("streamEventConsumer.Handle", runnerSource, StringComparison.Ordinal);
        Assert.Contains("new ChatStreamEventConsumerContext", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("case ControlPlaneConversationStreamEventKind.AssistantTextDelta", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("case ControlPlaneConversationStreamEventKind.TurnCompleted", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("case ControlPlaneConversationStreamEventKind.ServerRequestResolved", runnerSource, StringComparison.Ordinal);

        Assert.Contains("case ControlPlaneConversationStreamEventKind.AssistantTextDelta", consumerSource, StringComparison.Ordinal);
        Assert.Contains("case ControlPlaneConversationStreamEventKind.TurnCompleted", consumerSource, StringComparison.Ordinal);
        Assert.Contains("case ControlPlaneConversationStreamEventKind.ServerRequestResolved", consumerSource, StringComparison.Ordinal);
        Assert.Contains("WriteProjectionCommittedBlocks", consumerSource, StringComparison.Ordinal);
        Assert.Contains("PendingInteractiveAutomation", consumerSource, StringComparison.Ordinal);
    }

    [Fact]
    public void InteractiveChatRunner_FollowUpCommands_UseDedicatedCommandHandler()
    {
        var root = FindRepositoryRoot();
        var runnerSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Host",
            "InteractiveChatSessionHost.cs"));
        var handlerSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Commands",
            "FollowUp",
            "InteractiveFollowUpCommandHandler.cs"));

        Assert.Contains("InteractiveFollowUpCommandHandler", runnerSource, StringComparison.Ordinal);
        Assert.Contains("followUpCommandHandler.HandleFollowUpAsync", runnerSource, StringComparison.Ordinal);
        Assert.Contains("followUpCommandHandler.TryExecuteRunningFollowUpInputAsync", runnerSource, StringComparison.Ordinal);
        Assert.Contains("followUpCommandHandler.HandleSendRestoredFollowUp", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private Task HandleFollowUpAsync", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private Task<bool> TryExecuteRunningFollowUpInputAsync", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private void HandleSendRestoredFollowUp", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("恢复草稿发送失败，已保留当前草稿。", runnerSource, StringComparison.Ordinal);

        Assert.Contains("HandleFollowUpAsync", handlerSource, StringComparison.Ordinal);
        Assert.Contains("TryExecuteRunningFollowUpInputAsync", handlerSource, StringComparison.Ordinal);
        Assert.Contains("HandleSendRestoredFollowUp", handlerSource, StringComparison.Ordinal);
        Assert.Contains("RestoreDraftAfterDispatchFailure", handlerSource, StringComparison.Ordinal);
    }

    [Fact]
    public void InteractiveChatRunner_ThreadResumeLifecycle_UsesDedicatedCoordinator()
    {
        var root = FindRepositoryRoot();
        var runnerSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Host",
            "InteractiveChatSessionHost.cs"));
        var coordinatorSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Orchestration",
            "ThreadResumeCoordinator.cs"));

        Assert.Contains("ThreadResumeCoordinator", runnerSource, StringComparison.Ordinal);
        Assert.Contains("threadResumeCoordinator.ResumeThreadByIdAsync", runnerSource, StringComparison.Ordinal);
        Assert.Contains("threadResumeCoordinator.TryConsumeStartupResumedThreadStateAsync", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private void ConsumeResumedThreadState", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private void ReplayPendingInteractiveRequests", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private void RestorePendingFollowUps", runnerSource, StringComparison.Ordinal);

        Assert.Contains("ConsumeResumedThreadState", coordinatorSource, StringComparison.Ordinal);
        Assert.Contains("ReplayPendingInteractiveRequests", coordinatorSource, StringComparison.Ordinal);
        Assert.Contains("RestorePendingFollowUps", coordinatorSource, StringComparison.Ordinal);
        Assert.Contains("TryConsumeStartupResumedThreadStateAsync", coordinatorSource, StringComparison.Ordinal);
    }

    [Fact]
    public void InteractiveChatRunner_ConversationOperations_UseDedicatedCoordinator()
    {
        var root = FindRepositoryRoot();
        var runnerSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Host",
            "InteractiveChatSessionHost.cs"));
        var coordinatorSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Orchestration",
            "ConversationOperationCoordinator.cs"));

        Assert.Contains("ConversationOperationCoordinator", runnerSource, StringComparison.Ordinal);
        Assert.Contains("conversationOperationCoordinator.Start", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("CliConversationInterrupted", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("CliConversationCompleted", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("CliConversationException", runnerSource, StringComparison.Ordinal);

        Assert.Contains("CliConversationInterrupted", coordinatorSource, StringComparison.Ordinal);
        Assert.Contains("CliConversationCompleted", coordinatorSource, StringComparison.Ordinal);
        Assert.Contains("CliConversationException", coordinatorSource, StringComparison.Ordinal);
        Assert.Contains("StartWorkingDockTimer", coordinatorSource, StringComparison.Ordinal);
        Assert.Contains("StopWorkingDockTimer", coordinatorSource, StringComparison.Ordinal);
    }

    [Fact]
    public void InteractiveChatRunner_GovernanceResponses_UseDedicatedCommandHandler()
    {
        var root = FindRepositoryRoot();
        var runnerSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Host",
            "InteractiveChatSessionHost.cs"));
        var handlerSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Commands",
            "Governance",
            "InteractiveGovernanceCommandHandler.cs"));

        Assert.Contains("InteractiveGovernanceCommandHandler", runnerSource, StringComparison.Ordinal);
        Assert.Contains("governanceCommandHandler.HandleApprovalAsync", runnerSource, StringComparison.Ordinal);
        Assert.Contains("governanceCommandHandler.HandlePermissionRequestAsync", runnerSource, StringComparison.Ordinal);
        Assert.Contains("governanceCommandHandler.HandleUserInputAsync", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task HandleApprovalAsync", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task HandlePermissionRequestAsync", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task HandleUserInputAsync", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("解析权限响应 JSON 失败", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("解析补录 JSON 失败", runnerSource, StringComparison.Ordinal);

        Assert.Contains("CliApprovalResponseResolver.BuildResolution", handlerSource, StringComparison.Ordinal);
        Assert.Contains("ProbePermissionRequestScript.ParseJson", handlerSource, StringComparison.Ordinal);
        Assert.Contains("ProbeUserInputScript.ParseJson", handlerSource, StringComparison.Ordinal);
        Assert.Contains("pendingRequests.RemoveApproval", handlerSource, StringComparison.Ordinal);
        Assert.Contains("pendingRequests.RemovePermission", handlerSource, StringComparison.Ordinal);
        Assert.Contains("pendingRequests.RemoveUserInput", handlerSource, StringComparison.Ordinal);
    }

    [Fact]
    public void InteractiveChatRunner_WaitCommands_UseDedicatedCommandHandler()
    {
        var root = FindRepositoryRoot();
        var runnerSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Host",
            "InteractiveChatSessionHost.cs"));
        var handlerSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Commands",
            "Wait",
            "InteractiveWaitCommandHandler.cs"));

        Assert.Contains("InteractiveWaitCommandHandler", runnerSource, StringComparison.Ordinal);
        Assert.Contains("waitCommandHandler.HandleWaitAsync", runnerSource, StringComparison.Ordinal);
        Assert.Contains("waitCommandHandler.HandleWaitEventAsync", runnerSource, StringComparison.Ordinal);
        Assert.Contains("waitCommandHandler.HandleWaitNextToolCallAsync", runnerSource, StringComparison.Ordinal);
        Assert.Contains("waitCommandHandler.HandleWaitCompleteAsync", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task HandleWaitAsync", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task HandleWaitEventAsync", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private Task HandleWaitNextToolCallAsync", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task HandleWaitCompleteAsync", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ParseWaitTimeoutSeconds", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("TryParseEventKindToken", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadToolCallPhase", runnerSource, StringComparison.Ordinal);

        Assert.Contains("ParseWaitTimeoutSeconds", handlerSource, StringComparison.Ordinal);
        Assert.Contains("TryParseEventKindToken", handlerSource, StringComparison.Ordinal);
        Assert.Contains("ReadToolCallPhase", handlerSource, StringComparison.Ordinal);
        Assert.Contains("WaitForIdleAsync", handlerSource, StringComparison.Ordinal);
    }

    [Fact]
    public void InteractiveChatRunner_StateAndConfigCommands_UseDedicatedHandlers()
    {
        var root = FindRepositoryRoot();
        var runnerSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Host",
            "InteractiveChatSessionHost.cs"));
        var stateHandlerSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Commands",
            "State",
            "InteractiveStateCommandHandler.cs"));
        var configHandlerSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Commands",
            "Config",
            "InteractiveConfigCommandHandler.cs"));

        Assert.Contains("InteractiveStateCommandHandler", runnerSource, StringComparison.Ordinal);
        Assert.Contains("InteractiveConfigCommandHandler", runnerSource, StringComparison.Ordinal);
        Assert.Contains("stateCommandHandler.HandleStateCommandAsync", runnerSource, StringComparison.Ordinal);
        Assert.Contains("configCommandHandler.HandleConfigCommandAsync", runnerSource, StringComparison.Ordinal);
        Assert.Contains("configCommandHandler.HandleConfigReloadCommandAsync", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task HandleStateCommandAsync", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task HandleConfigCommandAsync", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task HandleConfigReloadCommandAsync", runnerSource, StringComparison.Ordinal);

        Assert.Contains("pendingApprovalCallIds", stateHandlerSource, StringComparison.Ordinal);
        Assert.Contains("restoredPendingFollowUpCorrelations", stateHandlerSource, StringComparison.Ordinal);
        Assert.Contains("ResumeThreadAsync", configHandlerSource, StringComparison.Ordinal);
        Assert.Contains("LoadResolvedConfig", configHandlerSource, StringComparison.Ordinal);
    }

    [Fact]
    public void InteractiveChatRunner_ModelSelection_UsesDedicatedCommandHandler()
    {
        var root = FindRepositoryRoot();
        var runnerSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Host",
            "InteractiveChatSessionHost.cs"));
        var handlerSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Commands",
            "Model",
            "InteractiveModelCommandHandler.cs"));

        Assert.Contains("InteractiveModelCommandHandler", runnerSource, StringComparison.Ordinal);
        Assert.Contains("modelCommandHandler.HandleModelCommandAsync", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task HandleModelCommandAsync", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task SelectModelRouteSetAsync", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("TryParseModelStatusCommand", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("TrySelectRouteSetWithTianShuTerminalAsync", runnerSource, StringComparison.Ordinal);

        Assert.Contains("TryParseModelStatusCommand", handlerSource, StringComparison.Ordinal);
        Assert.Contains("TrySelectRouteSetWithTianShuTerminalAsync", handlerSource, StringComparison.Ordinal);
        Assert.Contains("SelectModelRouteSetAsync", handlerSource, StringComparison.Ordinal);
        Assert.Contains("PersistSelectedRouteSetAsync", handlerSource, StringComparison.Ordinal);
        Assert.Contains("ListModelRouteSets", handlerSource, StringComparison.Ordinal);
        Assert.Contains("ResumeThreadAsync", handlerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("KernelConfigCompatibilityReaders", handlerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("KernelConfigCompatibilityUtilities", handlerSource, StringComparison.Ordinal);
    }

    [Fact]
    public void InteractiveChatRunner_RpcCommand_UsesDedicatedCommandHandler()
    {
        var root = FindRepositoryRoot();
        var runnerSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Host",
            "InteractiveChatSessionHost.cs"));
        var handlerSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Commands",
            "Rpc",
            "InteractiveRpcCommandHandler.cs"));

        Assert.Contains("InteractiveRpcCommandHandler", runnerSource, StringComparison.Ordinal);
        Assert.Contains("rpcCommandHandler.HandleRpcAsync", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task HandleRpcAsync", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("解析 RPC 参数 JSON 失败", runnerSource, StringComparison.Ordinal);

        Assert.Contains("TryInvokeFormalRpcAsync", handlerSource, StringComparison.Ordinal);
        Assert.Contains("BuildFormalRpcUnavailableMessage", handlerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("IsLegacyDiagnosticsBridgeMethod", handlerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("InvokeDiagnosticRpcAsync", handlerSource, StringComparison.Ordinal);
    }

    [Fact]
    public void TerminalPromptRenderer_ClearFrame_ClearsPromptAndFooterLines()
    {
        var sourcePath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Presentations",
            "TianShu.Cli",
            "Terminal",
            "TerminalPromptRenderer.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("public string ClearFrame()", source, StringComparison.Ordinal);
        Assert.Contains("previousExtraLineCount = 0;", source, StringComparison.Ordinal);
        Assert.Contains("builder.Append(ClearCurrentLine)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FixedComposerDockController_IsNotUsedByCliPresentation()
    {
        var sourcePath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Rendering",
            "FixedComposerDockController.cs");

        Assert.False(File.Exists(sourcePath));
    }

    [Fact]
    public void InteractiveChatRunner_ModelPicker_EscapeDoesNotFallBackToTextListOrTranscript()
    {
        var sourcePath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Commands",
            "Model",
            "InteractiveModelCommandHandler.cs");
        var source = File.ReadAllText(sourcePath);
        var modelCommandStart = source.IndexOf("public async Task HandleModelCommandAsync", StringComparison.Ordinal);
        var selectModelStart = source.IndexOf("private static async Task SelectModelRouteSetAsync", modelCommandStart, StringComparison.Ordinal);

        Assert.True(modelCommandStart >= 0);
        Assert.True(selectModelStart > modelCommandStart);

        var modelCommandSource = source[modelCommandStart..selectModelStart];

        Assert.Contains("if (context.ShouldUseInteractivePicker() && routeSets.Length > 0)", modelCommandSource, StringComparison.Ordinal);
        Assert.Contains("if (selectedRouteSet is null)", modelCommandSource, StringComparison.Ordinal);
        Assert.Contains("return;", modelCommandSource, StringComparison.Ordinal);
        Assert.Contains("context.WriteControlPlaneLine(\"可用模型路由方案：\", false)", modelCommandSource, StringComparison.Ordinal);
        Assert.DoesNotContain("WriteLine(\"可用模型路由方案：\")", modelCommandSource, StringComparison.Ordinal);
    }

    [Fact]
    public void InteractiveChatRunner_ModelStatusLiveRefresh_HidesConsoleCursorDuringStyledBatch()
    {
        var handlerPath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Commands",
            "ModelStatus",
            "ModelStatusCommandHandler.cs");
        var handlerSource = File.ReadAllText(handlerPath);

        Assert.Contains("using (output.HideCursorForTerminalRefresh())", handlerSource, StringComparison.Ordinal);
        Assert.Contains("using var exclusiveFrameScope = output.BeginExclusiveFrameScope()", handlerSource, StringComparison.Ordinal);
        Assert.Contains("output.WriteLiveRows(BuildProbeRows(probes, output.Styled), false)", handlerSource, StringComparison.Ordinal);
        Assert.Contains("output.WriteLiveRows(BuildProbeRows(probes, output.Styled), true)", handlerSource, StringComparison.Ordinal);

        var writerPath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Commands",
            "ModelStatus",
            "ModelStatusConsoleWriter.cs");
        var writerSource = File.ReadAllText(writerPath);

        Assert.Contains("TerminalConsoleRefreshScope.HideCursorForRefresh", writerSource, StringComparison.Ordinal);
        Assert.Contains("beginCommandOverlay", writerSource, StringComparison.Ordinal);
        Assert.Contains("setCommandOverlayLines", writerSource, StringComparison.Ordinal);

        var hostPath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Host",
            "TerminalConsoleRefreshScope.cs");
        var hostSource = File.ReadAllText(hostPath);

        Assert.Contains("Console.CursorVisible = false", hostSource, StringComparison.Ordinal);
        Assert.Contains("Console.CursorVisible = previous", hostSource, StringComparison.Ordinal);

        var runnerPath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Host",
            "InteractiveChatSessionHost.cs");
        var runnerSource = File.ReadAllText(runnerPath);

        Assert.DoesNotContain("HideConsoleCursorForTerminalRefresh", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Console.CursorVisible = false", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Console.CursorVisible = previous", runnerSource, StringComparison.Ordinal);
    }

    [Fact]
    public void InteractiveChatRunner_ModelStatusRows_PublishStyledRowsToCommandOverlay()
    {
        var sourcePath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Commands",
            "ModelStatus",
            "ModelStatusConsoleWriter.cs");
        var writerSource = File.ReadAllText(sourcePath);

        Assert.Contains("WriteLiveBatchRows(", writerSource, StringComparison.Ordinal);
        Assert.Contains("IReadOnlyList<string> rows", writerSource, StringComparison.Ordinal);
        Assert.Contains("WriteOverlayLiveRowsUnsafe", writerSource, StringComparison.Ordinal);
        Assert.Contains("renderer.FitTerminalRow(row, styled: true)", writerSource, StringComparison.Ordinal);
        Assert.Contains("setCommandOverlayLines", writerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("DisableAutoWrap(styled)", writerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Console.Write(\"\\u001b[?7l\")", writerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Console.Write(\"\\u001b[?7h\")", writerSource, StringComparison.Ordinal);

        var runnerPath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Host",
            "InteractiveChatSessionHost.cs");
        var runnerSource = File.ReadAllText(runnerPath);

        Assert.DoesNotContain("WriteModelStatus", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildModelStatusProbeRow", runnerSource, StringComparison.Ordinal);

        var rendererPath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Rendering",
            "ModelStatusTableRenderer.cs");
        var rendererSource = File.ReadAllText(rendererPath);

        Assert.Contains("public string BuildRow(", rendererSource, StringComparison.Ordinal);
        Assert.Contains("public string FitTerminalRow(", rendererSource, StringComparison.Ordinal);
    }

    [Fact]
    public void InteractiveChatRunner_ModelStatusProbe_UsesConfiguredPromptWhenAvailable()
    {
        var sourcePath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Commands",
            "ModelStatus",
            "ModelStatusCommandHandler.cs");
        var source = File.ReadAllText(sourcePath);
        var probeStart = source.IndexOf("async Task<(ProviderModelConnectivityProbeItem? Item, TimeSpan Elapsed)> ProbeItemAsync", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private string[] BuildProbeRows", probeStart, StringComparison.Ordinal);

        Assert.True(probeStart >= 0);
        Assert.True(methodEnd > probeStart);

        var probeSource = source[probeStart..methodEnd];

        Assert.Contains("TianShuPromptConfigUtilities.FromConfig(snapshot.Config.RawConfig)", probeSource, StringComparison.Ordinal);
        Assert.Contains("Prompt = promptConfiguration.ModelStatusReasoningProbePrompt ?? DefaultReasoningProbePrompt", probeSource, StringComparison.Ordinal);
        Assert.Contains("Timeout = TimeSpan.FromSeconds(30)", probeSource, StringComparison.Ordinal);
    }

    [Fact]
    public void InteractiveChatRunner_ThreadLifecycleCommands_UseDedicatedHandlerForDeleteAndClear()
    {
        var root = FindRepositoryRoot();
        var runnerSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Host",
            "InteractiveChatSessionHost.cs"));
        var handlerSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Commands",
            "Threads",
            "InteractiveThreadCommandHandler.cs"));

        Assert.Contains("InteractiveThreadCommandHandler", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("HandleDeleteThreadAsync", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("HandleClearThreadsAsync", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ExtractTrailingConfirmation", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadNamedOption", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ControlPlaneDeleteThreadCommand", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ControlPlaneClearThreadsCommand", runnerSource, StringComparison.Ordinal);

        Assert.Contains("HandleDeleteThreadAsync", handlerSource, StringComparison.Ordinal);
        Assert.Contains("HandleClearThreadsAsync", handlerSource, StringComparison.Ordinal);
        Assert.Contains("ExtractTrailingConfirmation", handlerSource, StringComparison.Ordinal);
        Assert.Contains("ReadNamedOption", handlerSource, StringComparison.Ordinal);
        Assert.Contains("ControlPlaneDeleteThreadCommand", handlerSource, StringComparison.Ordinal);
        Assert.Contains("ControlPlaneClearThreadsCommand", handlerSource, StringComparison.Ordinal);
    }

    [Fact]
    public void InteractiveChatRunner_StartupThreadSelection_UsesDedicatedSelector()
    {
        var root = FindRepositoryRoot();
        var runnerSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Host",
            "InteractiveChatSessionHost.cs"));
        var selectorSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Commands",
            "Threads",
            "StartupThreadSelector.cs"));

        Assert.Contains("StartupThreadSelector", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ResolveStartupThreadSelectionAsync", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static async Task<ControlPlaneThreadSummary?> TrySelectThreadWithTianShuTerminalAsync", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Limit = 100", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("找到多个同名线程", runnerSource, StringComparison.Ordinal);

        Assert.Contains("ResolveSelectionAsync", selectorSource, StringComparison.Ordinal);
        Assert.Contains("TrySelectThreadWithTianShuTerminalAsync", selectorSource, StringComparison.Ordinal);
        Assert.Contains("Func<IDisposable>? beginExclusiveFrameScope", selectorSource, StringComparison.Ordinal);
        Assert.Contains("Limit = limit", selectorSource, StringComparison.Ordinal);
        Assert.Contains("找到多个同名线程", selectorSource, StringComparison.Ordinal);
    }

    [Fact]
    public void InteractiveChatRunner_PendingInteractiveReplay_UsesDedicatedCoordinator()
    {
        var root = FindRepositoryRoot();
        var runnerSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Host",
            "InteractiveChatSessionHost.cs"));
        var coordinatorSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Orchestration",
            "PendingInteractiveReplayCoordinator.cs"));

        Assert.Contains("PendingInteractiveReplayCoordinator", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildPendingInteractiveReplayEvent", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("\"approval_requested\" => new ControlPlaneConversationStreamEvent", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("\"permission_requested\" => new ControlPlaneConversationStreamEvent", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("\"request_user_input\" => new ControlPlaneConversationStreamEvent", runnerSource, StringComparison.Ordinal);

        Assert.Contains("BuildReplayEvents", coordinatorSource, StringComparison.Ordinal);
        Assert.Contains("\"approval_requested\" => new ControlPlaneConversationStreamEvent", coordinatorSource, StringComparison.Ordinal);
        Assert.Contains("\"permission_requested\" => new ControlPlaneConversationStreamEvent", coordinatorSource, StringComparison.Ordinal);
        Assert.Contains("\"request_user_input\" => new ControlPlaneConversationStreamEvent", coordinatorSource, StringComparison.Ordinal);
    }

    [Fact]
    public void InteractiveChatRunner_SlashCommandRouting_UsesDedicatedDispatcher()
    {
        var root = FindRepositoryRoot();
        var runnerSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Host",
            "InteractiveChatSessionHost.cs"));
        var dispatcherSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Commands",
            "ChatSlashCommandDispatcher.cs"));
        var handlerRegistrySource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Commands",
            "ChatSlashCommandHandlerRegistry.cs"));

        Assert.Contains("ChatSlashCommandDispatcher", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("SlashCommandClassifier.Classify(line)", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("switch (classified.Kind)", runnerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("case SlashCommandKind.Help", runnerSource, StringComparison.Ordinal);

        Assert.Contains("SlashCommandClassifier.Classify(line)", dispatcherSource, StringComparison.Ordinal);
        Assert.Contains("handlers.TryGetHandler(classified.Kind", dispatcherSource, StringComparison.Ordinal);
        Assert.DoesNotContain("switch (classified.Kind)", dispatcherSource, StringComparison.Ordinal);
        Assert.DoesNotContain("case SlashCommandKind.Help", dispatcherSource, StringComparison.Ordinal);
        Assert.Contains("[SlashCommandKind.Help]", handlerRegistrySource, StringComparison.Ordinal);
        Assert.Contains("ChatSlashCommandHandlerRegistry", handlerRegistrySource, StringComparison.Ordinal);
    }

    [Fact]
    public void InteractiveChatRunner_Source_NoLongerReferencesTerminalGuiChatSurface()
    {
        var sourcePath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Host",
            "InteractiveChatSessionHost.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.DoesNotContain("TerminalGuiChatSurface", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Terminal.Gui", source, StringComparison.Ordinal);
        Assert.Null(CliAssembly.GetType("TianShu.Cli.InteractiveChatRunner+TerminalGuiChatSurface", throwOnError: false));
    }

    [Fact]
    public void TerminalPromptRenderer_RendersTianShuInputShellWithoutTerminalGui()
    {
        var renderer = new TerminalPromptRenderer();
        var frame = new TerminalRenderFrame("> ", "abc", 1);

        var output = renderer.Render(frame);

        Assert.StartsWith("\r\u001b[2K" + TerminalAnsi.Reset + "> abc", output, StringComparison.Ordinal);
        Assert.EndsWith("\r\u001b[3C", output, StringComparison.Ordinal);
    }

    [Fact]
    public void TerminalSelectionPicker_BuildFrame_RendersThreadOverlayWithoutIds()
    {
        var frame = TerminalSelectionPicker.BuildFrame(["first title", "second title"], "选择要恢复的线程", selectedIndex: 0);

        Assert.Contains("选择要恢复的线程", frame, StringComparison.Ordinal);
        Assert.Contains("> first title", frame, StringComparison.Ordinal);
        Assert.Contains("  second title", frame, StringComparison.Ordinal);
        Assert.DoesNotContain("thread_", frame, StringComparison.Ordinal);
    }

    [Fact]
    public void TerminalSelectionPicker_UsesExclusiveFrameScopeForTemporaryOverlay()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Presentations",
            "TianShu.Cli",
            "Terminal",
            "TerminalSelectionPicker.cs"));

        Assert.Contains("Func<IDisposable>? beginExclusiveFrameScope", source, StringComparison.Ordinal);
        Assert.Contains("using var exclusiveFrameScope", source, StringComparison.Ordinal);
        Assert.Contains("finally", source, StringComparison.Ordinal);
        Assert.Contains("Clear(renderedLineCount)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ModelSelectionRow_WhenDisplayNameMatchesModel_ShouldNotDuplicateModelName()
    {
        var row = SelectionPickerRowRenderer.BuildModelSelectionRow(new ControlPlaneModelCatalogItem
        {
            Model = "gpt-5.4",
            DisplayName = "gpt-5.4",
            IsDefault = true,
        });

        Assert.Equal("gpt-5.4  default", row);
    }

    [Fact]
    public void TerminalStartupBanner_Build_ShowsProductContext()
    {
        using var tempDir = new TestTempDirectory();
        var configPath = WriteStartupBannerConfig(tempDir.Path, "openai-compatible-default", "openai-compatible", "openai_chat_completions");
        var options = CreateChatOptions();
        ReflectionTestHelper.SetProperty(options, "RuntimeModel", "gpt-test high");
        ReflectionTestHelper.SetProperty(options, "ConfigFilePath", configPath);
        ReflectionTestHelper.SetProperty(options, "WorkingDirectory", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        ReflectionTestHelper.SetProperty(options, "DangerouslyBypassApprovalsAndSandbox", true);

        var banner = TerminalStartupBanner.Build((ChatCommandOptions)options);
        var bannerLines = SplitBannerLines(banner);

        Assert.Contains("天枢 TianShu", banner, StringComparison.Ordinal);
        Assert.StartsWith("━", bannerLines[0], StringComparison.Ordinal);
        Assert.Contains("天枢 TianShu", bannerLines[1], StringComparison.Ordinal);
        Assert.DoesNotContain("▄████", banner, StringComparison.Ordinal);
        Assert.DoesNotContain("◀████", banner, StringComparison.Ordinal);
        Assert.DoesNotContain("▀████", banner, StringComparison.Ordinal);
        Assert.Contains("天枢 TianShu", banner, StringComparison.Ordinal);
        Assert.DoesNotContain(">_", banner, StringComparison.Ordinal);
        Assert.DoesNotContain("model:", banner, StringComparison.Ordinal);
        Assert.Contains("工作区：   ~", banner, StringComparison.Ordinal);
        Assert.DoesNotContain("协议：", banner, StringComparison.Ordinal);
        Assert.Contains("权限：     完全访问（无需审批）", banner, StringComparison.Ordinal);
        Assert.Contains("提示：     输入 /help 查看命令，/model-route 切换模型路由方案，@file 附加上下文。", banner, StringComparison.Ordinal);
        Assert.DoesNotContain("workspace:", banner, StringComparison.Ordinal);
        Assert.DoesNotContain("access:", banner, StringComparison.Ordinal);
        Assert.DoesNotContain("hints:", banner, StringComparison.Ordinal);
        Assert.DoesNotContain("YOLO", banner, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("━", bannerLines[^2], StringComparison.Ordinal);
    }

    [Fact]
    public void TerminalStartupBanner_BuildLogoModel_ExposesTypedStartupFields()
    {
        using var tempDir = new TestTempDirectory();
        var configPath = WriteStartupBannerConfig(tempDir.Path, "openai-compatible-default", "openai-compatible", "openai_chat_completions");
        var options = CreateChatOptions();
        ReflectionTestHelper.SetProperty(options, "RuntimeModel", "gpt-test high");
        ReflectionTestHelper.SetProperty(options, "ConfigFilePath", configPath);
        ReflectionTestHelper.SetProperty(options, "WorkingDirectory", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        ReflectionTestHelper.SetProperty(options, "DangerouslyBypassApprovalsAndSandbox", true);

        var model = TerminalStartupBanner.BuildLogoModel((ChatCommandOptions)options);

        Assert.Equal("天枢 TianShu", model.ProductName);
        Assert.Equal("~", model.Directory);
        Assert.Equal("openai_chat_completions", model.Protocol);
        Assert.Equal("never", model.Approval);
        Assert.Equal("danger-full-access", model.Sandbox);
        Assert.Equal("输入 /help 查看命令，/model-route 切换模型路由方案，@file 附加上下文。", model.Tip);
    }

    [Fact]
    public void TerminalStartupBanner_BuildLogoModel_UsesRuntimeProviderWireApiWhenResolved()
    {
        var options = CreateChatOptions();
        ReflectionTestHelper.SetProperty(options, "RuntimeProviderWireApi", "anthropic_messages");

        var model = TerminalStartupBanner.BuildLogoModel((ChatCommandOptions)options);
        var banner = TerminalStartupBanner.Build((ChatCommandOptions)options);

        Assert.Equal("anthropic_messages", model.Protocol);
        Assert.DoesNotContain("协议：", banner, StringComparison.Ordinal);
    }

    [Fact]
    public void TerminalStartupBanner_TypedModelAndRenderers_AreSplitFromFacade()
    {
        var root = FindRepositoryRoot();
        var terminalRoot = Path.Combine(
            root,
            "src",
            "Presentations",
            "TianShu.Cli",
            "Terminal");
        var facadeSource = File.ReadAllText(Path.Combine(terminalRoot, "TerminalStartupBanner.cs"));
        var modelSource = File.ReadAllText(Path.Combine(terminalRoot, "StartupLogoModel.cs"));
        var logoRendererSource = File.ReadAllText(Path.Combine(terminalRoot, "StartupLogoRenderer.cs"));
        var placeholderRendererSource = File.ReadAllText(Path.Combine(terminalRoot, "StartupPlaceholderRenderer.cs"));

        Assert.Contains("internal sealed record StartupLogoModel", modelSource, StringComparison.Ordinal);
        Assert.Contains("StartupLogoRenderer.Build(BuildModel(options)", facadeSource, StringComparison.Ordinal);
        Assert.Contains("StartupPlaceholderRenderer.BuildPlaceholder", facadeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("internal sealed record StartupLogoModel", facadeSource, StringComparison.Ordinal);
        Assert.Contains("StartupLogoModel model", logoRendererSource, StringComparison.Ordinal);
        Assert.Contains("BuildFooter(StartupLogoModel model", placeholderRendererSource, StringComparison.Ordinal);
    }

    [Fact]
    public void TerminalTranscriptRenderer_DoesNotUseVisibleToolTitleForSemanticDecisions()
    {
        var sourcePath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Rendering",
            "TerminalTranscriptRenderer.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("ToolPresentationKind.Command", source, StringComparison.Ordinal);
        Assert.Contains("block.Kind", source, StringComparison.Ordinal);
        Assert.DoesNotContain("block.Title == \"执行命令\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("string.Equals(block.Title", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CliToolPresentation_UsesTypedPayloadBeforeLegacyRawSummaryBuilder()
    {
        var root = FindRepositoryRoot();
        var presenterSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Presenters",
            "ToolInvocationPresenter.cs"));
        var projectorSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Projection",
            "ChatPresentationProjector.cs"));
        var eventSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Events",
            "CliInteractionEvent.cs"));

        Assert.Contains("ToolInvocationPayload? Payload", eventSource, StringComparison.Ordinal);
        Assert.Contains("snapshot.Payload", projectorSource, StringComparison.Ordinal);
        Assert.Contains("payload?.Input?.Subject ?? BuildInputSummary", presenterSource, StringComparison.Ordinal);
        Assert.Contains("payload?.Output?.Summary", presenterSource, StringComparison.Ordinal);
        Assert.Contains("?? BuildOutputSummary", presenterSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ToolInvocationSummaryBuilder_IsLegacyAdapterOverTypedPayload()
    {
        var sourcePath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Presenters",
            "ToolInvocationSummaryBuilder.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("ToolInvocationPayload.Create", source, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonDocument.Parse", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TryParseJsonObject", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadJsonCommandSummary", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ToolInvocationPayload_FileStaysAsTypedAggregateWithoutParserDetails()
    {
        var root = FindRepositoryRoot();
        var payloadSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Events",
            "ToolInvocationPayload.cs"));
        var inputParserPath = Path.Combine(
            root,
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Events",
            "ToolInvocationInput.cs");
        var outputParserPath = Path.Combine(
            root,
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Events",
            "ToolInvocationOutput.cs");
        var jsonHelpersPath = Path.Combine(
            root,
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Events",
            "ToolInvocationJsonHelpers.cs");

        Assert.True(File.Exists(inputParserPath));
        Assert.True(File.Exists(outputParserPath));
        Assert.True(File.Exists(jsonHelpersPath));
        Assert.Contains("ToolInvocationPayload Create", payloadSource, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonDocument", payloadSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadJsonCommandSummary", payloadSource, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildPlanStepSummary", payloadSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ToolInvocationInput_FileStaysAsFacadeOverDomainParsers()
    {
        var root = FindRepositoryRoot();
        var inputSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Events",
            "ToolInvocationInput.cs"));
        var commandParserPath = Path.Combine(
            root,
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Events",
            "ToolInvocationCommandInputParser.cs");
        var fileParserPath = Path.Combine(
            root,
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Events",
            "ToolInvocationFileInputParser.cs");
        var genericParserPath = Path.Combine(
            root,
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Events",
            "ToolInvocationGenericInputParser.cs");
        var planParserPath = Path.Combine(
            root,
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Events",
            "ToolInvocationPlanInputParser.cs");

        Assert.True(File.Exists(commandParserPath));
        Assert.True(File.Exists(fileParserPath));
        Assert.True(File.Exists(genericParserPath));
        Assert.True(File.Exists(planParserPath));
        Assert.Contains("ToolInvocationCommandInputParser.Build", inputSource, StringComparison.Ordinal);
        Assert.Contains("ToolInvocationFileInputParser.Build", inputSource, StringComparison.Ordinal);
        Assert.Contains("ToolInvocationPlanInputParser.Build", inputSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadJsonCommandSummary", inputSource, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildPlanStepSummary", inputSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadJsonStringArray", inputSource, StringComparison.Ordinal);
    }

    [Fact]
    public void CliEventNormalizer_DelegatesToolPayloadParsingToTypedToolNormalizer()
    {
        var root = FindRepositoryRoot();
        var normalizerSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Events",
            "CliEventNormalizer.cs"));
        var toolNormalizerPath = Path.Combine(
            root,
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Events",
            "CliToolInvocationEventNormalizer.cs");
        var payloadReaderPath = Path.Combine(
            root,
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Events",
            "CliStructuredPayloadReader.cs");

        Assert.True(File.Exists(toolNormalizerPath));
        Assert.True(File.Exists(payloadReaderPath));
        Assert.Contains("CliToolInvocationEventNormalizer.BuildToolInvocationEvent", normalizerSource, StringComparison.Ordinal);
        Assert.Contains("CliToolInvocationEventNormalizer.TryBuildItemToolInvocationEvent", normalizerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadToolPayloadString", normalizerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadItemPayloadString", normalizerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("TryReadObjectPropertyIgnoreCase", normalizerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("LooksLikeToolItem", normalizerSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatPresentationProjector_DelegatesToolProjectionPolicy()
    {
        var root = FindRepositoryRoot();
        var projectorSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Projection",
            "ChatPresentationProjector.cs"));
        var policyPath = Path.Combine(
            root,
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Projection",
            "ToolInvocationProjectionPolicy.cs");

        Assert.True(File.Exists(policyPath));
        Assert.Contains("ToolInvocationProjectionPolicy.IsInternalToolEvent", projectorSource, StringComparison.Ordinal);
        Assert.Contains("ToolInvocationProjectionPolicy.ResolveCompletionKey", projectorSource, StringComparison.Ordinal);
        Assert.Contains("ToolInvocationProjectionPolicy.ShouldSuppressCompletedDisplay", projectorSource, StringComparison.Ordinal);
        Assert.DoesNotContain("\"request_approval\"", projectorSource, StringComparison.Ordinal);
        Assert.DoesNotContain("\"request_permission\"", projectorSource, StringComparison.Ordinal);
        Assert.DoesNotContain("\"request_user_input\"", projectorSource, StringComparison.Ordinal);
        Assert.DoesNotContain("\"update_plan\"", projectorSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ToolOutputLooksLikeFailure", projectorSource, StringComparison.Ordinal);
    }

    [Fact]
    public void TerminalMarkdownRenderer_DelegatesDenseLineAndSpacingNormalization()
    {
        var root = FindRepositoryRoot();
        var rendererSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Rendering",
            "TerminalMarkdownRenderer.cs"));
        var denseExpanderPath = Path.Combine(
            root,
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Rendering",
            "TerminalMarkdownDenseLineExpander.cs");
        var spacingNormalizerPath = Path.Combine(
            root,
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Rendering",
            "TerminalMarkdownInlineSpacingNormalizer.cs");

        Assert.True(File.Exists(denseExpanderPath));
        Assert.True(File.Exists(spacingNormalizerPath));
        Assert.Contains("TerminalMarkdownDenseLineExpander.Expand", rendererSource, StringComparison.Ordinal);
        Assert.Contains("TerminalMarkdownInlineSpacingNormalizer.Normalize", rendererSource, StringComparison.Ordinal);
        Assert.DoesNotContain("GeneratedRegex", rendererSource, StringComparison.Ordinal);
        Assert.DoesNotContain("DenseLabelRegex", rendererSource, StringComparison.Ordinal);
        Assert.DoesNotContain("DotNetSdkRegex", rendererSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ExpandDenseMarkdownLine", rendererSource, StringComparison.Ordinal);
    }

    [Fact]
    public void TerminalStartupBanner_BuildStyled_AddsPresentationColorsWithoutChangingPlainSchema()
    {
        using var tempDir = new TestTempDirectory();
        var configPath = WriteStartupBannerConfig(tempDir.Path, "openai-compatible-default", "openai-compatible", "auto");
        var options = CreateChatOptions();
        ReflectionTestHelper.SetProperty(options, "RuntimeModel", "gpt-test high");
        ReflectionTestHelper.SetProperty(options, "ConfigFilePath", configPath);
        ReflectionTestHelper.SetProperty(options, "WorkingDirectory", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        ReflectionTestHelper.SetProperty(options, "DangerouslyBypassApprovalsAndSandbox", true);

        var plain = TerminalStartupBanner.Build((ChatCommandOptions)options);
        var styled = TerminalStartupBanner.BuildStyled((ChatCommandOptions)options);
        var tip = TerminalStartupBanner.BuildTip(styled: true);

        Assert.DoesNotContain("\u001b[", plain, StringComparison.Ordinal);
        Assert.Contains("\u001b[", styled, StringComparison.Ordinal);
        Assert.Contains("\u001b[", tip, StringComparison.Ordinal);
        Assert.Contains("完全访问（无需审批）", styled, StringComparison.Ordinal);
        Assert.DoesNotContain("YOLO", styled, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TerminalStartupBanner_Build_UsesResolvedConfigModelWhenNoOverride()
    {
        using var tempDir = new TestTempDirectory();
        var configDirectory = Path.Combine(tempDir.Path, ".tianshu");
        Directory.CreateDirectory(configDirectory);
        var configPath = Path.Combine(configDirectory, "tianshu.toml");
        File.WriteAllText(
            configPath,
            """
            model = "gpt-config-test"
            provider = "openai"
            approval_policy = "on-request"
            sandbox_mode = "workspace-write"

            [providers.openai]
            base_url = "https://api.openai.com/v1"
            api_key_env = "OPENAI_API_KEY"
            default_protocol = "openai_responses"
            """);
        var options = CreateChatOptions();
        ReflectionTestHelper.SetProperty(options, "ConfigFilePath", configPath);
        ReflectionTestHelper.SetProperty(options, "WorkingDirectory", tempDir.Path);

        var banner = TerminalStartupBanner.Build((ChatCommandOptions)options);

        Assert.DoesNotContain("model:", banner, StringComparison.Ordinal);
        Assert.DoesNotContain("协议：", banner, StringComparison.Ordinal);
        Assert.Contains("权限：     工作区写入（按需审批）", banner, StringComparison.Ordinal);
    }

    [Fact]
    public void InstallTianShuCliScript_PreservesUserConfigByDefault()
    {
        var scriptPath = Path.Combine(
            FindRepositoryRoot(),
            "tools",
            "Install-TianShuCli.ps1");
        var source = File.ReadAllText(scriptPath);

        Assert.Contains("[switch]$OverwriteConfig", source, StringComparison.Ordinal);
        Assert.Contains("if ($OverwriteConfig -and -not $PreserveConfig)", source, StringComparison.Ordinal);
        Assert.Contains("保留已有配置：$userConfigPath", source, StringComparison.Ordinal);
        Assert.Contains("已按显式请求备份并更新配置", source, StringComparison.Ordinal);
        Assert.Contains("TIANSHU_HOME", source, StringComparison.Ordinal);
        Assert.Contains("TIANSHU_HOME", source, StringComparison.Ordinal);
        Assert.Contains("prompt-packs   = $promptPacksDirectory", source, StringComparison.Ordinal);
        Assert.DoesNotContain("prompt_file = \"default_prompt.toml\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("$userPromptPath", source, StringComparison.Ordinal);
        Assert.DoesNotContain("已备份并更新配置：$backupPath", source, StringComparison.Ordinal);
        Assert.DoesNotContain("已备份并更新 Prompt 配置：$backupPath", source, StringComparison.Ordinal);
    }

    [Fact]
    public void InstallTianShuCliScript_CleansInstallManagedConfigurationResidues()
    {
        var scriptPath = Path.Combine(
            FindRepositoryRoot(),
            "tools",
            "Install-TianShuCli.ps1");
        var source = File.ReadAllText(scriptPath);

        Assert.Contains("Remove-InstallManagedStalePath", source, StringComparison.Ordinal);
        Assert.Contains("$staleBinResourcesDirectory = Join-Path $binDirectory \"Resources\"", source, StringComparison.Ordinal);
        Assert.Contains("$legacyBuiltinToolsAssemblyPath = Join-Path $builtinToolsDirectory \"TianShu.Tooling.BuiltinTools.dll\"", source, StringComparison.Ordinal);
        Assert.Contains("$legacyBuiltinBundleDirectory = Join-Path $builtinToolsDirectory \"bundle\"", source, StringComparison.Ordinal);
        Assert.Contains("拒绝清理 TianShu Home 之外的安装残留", source, StringComparison.Ordinal);
        Assert.Contains("已清理安装残留", source, StringComparison.Ordinal);
    }

    [Fact]
    public void InstallTianShuCliScript_InstallsConfigGuiWithoutConfigOverwrite()
    {
        var scriptPath = Path.Combine(
            FindRepositoryRoot(),
            "tools",
            "Install-TianShuCli.ps1");
        var source = File.ReadAllText(scriptPath);

        Assert.Contains("TianShu.ConfigGui\\TianShu.ConfigGui.csproj", source, StringComparison.Ordinal);
        Assert.Contains("$configGuiPublishArgs = @(", source, StringComparison.Ordinal);
        Assert.Contains("开始发布天枢 TianShu ConfigGUI framework-dependent", source, StringComparison.Ordinal);
        Assert.Contains("--self-contained", source, StringComparison.Ordinal);
        Assert.Contains("\"false\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PublishProfile=win-x64-nativeaot", source, StringComparison.Ordinal);
        Assert.Contains("TianShu.ConfigGui.exe", source, StringComparison.Ordinal);
        Assert.Contains("Copy-PublishDirectory -SourceDirectory $configGuiPublishDirectory -TargetDirectory $binDirectory -Label \"ConfigGUI\"", source, StringComparison.Ordinal);
        Assert.Contains("ConfigGUI 已安装：$configGuiExePath", source, StringComparison.Ordinal);
        Assert.Contains("config-gui     = $configGuiExePath", source, StringComparison.Ordinal);
        Assert.Contains("$tianshuExePath = Join-Path $binDirectory \"tianshu.exe\"", source, StringComparison.Ordinal);
        Assert.Contains("Copy-Item -LiteralPath $publishedExePath -Destination $tianshuExePath -Force", source, StringComparison.Ordinal);
        Assert.Contains("executable     = $tianshuExePath", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Tian" + "ShuAliasExePath", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Tian" + "Shu-alias", source, StringComparison.Ordinal);
    }

    [Fact]
    public void InteractiveChatRunner_TerminalChatLoop_AddsVisualSpacingAroundTurns()
    {
        var loopSourcePath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Host",
            "TerminalChatInputLoop.cs");
        var runnerSourcePath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Host",
            "InteractiveChatSessionHost.cs");
        var terminalInputSourcePath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Host",
            "InteractiveChatSessionHost.TerminalInput.cs");
        var loopSource = File.ReadAllText(loopSourcePath);
        var runnerSource = File.ReadAllText(runnerSourcePath);
        var terminalInputSource = File.ReadAllText(terminalInputSourcePath);

        Assert.Contains("context.CompleteInputLine(renderer, true, action.Text)", loopSource, StringComparison.Ordinal);
        Assert.Contains("context.CompleteInputLine(renderer, false, null)", loopSource, StringComparison.Ordinal);
        Assert.Contains("clearSubmittedInput: !string.IsNullOrWhiteSpace(submittedText)", terminalInputSource, StringComparison.Ordinal);
        Assert.Contains("WriteTerminalVisualSpacerLine()", runnerSource, StringComparison.Ordinal);
        Assert.Contains("GetAssistantLeadingSpacerPending = () => assistantLeadingSpacerPending", runnerSource, StringComparison.Ordinal);
        Assert.Contains("HasRetainedTailFrame = () => terminalHost.HasRetainedTailFrame", runnerSource, StringComparison.Ordinal);
    }

    [Fact]
    public void InteractiveChatRunner_WorkingDockTimer_UsesBackgroundPeriodicRefreshWithoutBlockingConsoleGate()
    {
        var sourcePath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Host",
            "TerminalInteractionHost.cs");
        var source = File.ReadAllText(sourcePath);
        var tickStart = source.IndexOf("public void RefreshWorkingDockTick", StringComparison.Ordinal);
        var tickEnd = source.IndexOf("public bool PrepareInlineTailPromptWrite", tickStart, StringComparison.Ordinal);
        var workingTimerSourcePath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Host",
            "WorkingDockTimerController.cs");
        var workingTimerSource = File.ReadAllText(workingTimerSourcePath);

        Assert.True(tickStart >= 0);
        Assert.True(tickEnd > tickStart);
        Assert.Contains("internal sealed class WorkingDockTimerController", workingTimerSource, StringComparison.Ordinal);

        var tickSource = source[tickStart..tickEnd];

        Assert.Contains("Task.Run(", workingTimerSource, StringComparison.Ordinal);
        Assert.Contains("PeriodicTimer(TimeSpan.FromSeconds(1))", workingTimerSource, StringComparison.Ordinal);
        Assert.Contains("Monitor.TryEnter(syncRoot)", tickSource, StringComparison.Ordinal);
        Assert.Contains("assistantTail.HasUncommittedText()", tickSource, StringComparison.Ordinal);
        Assert.Contains("shouldSkipWorkingDockRefresh()", tickSource, StringComparison.Ordinal);
        Assert.Contains("using var cursorScope = hideCursorForRefresh();", File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Host",
            "TerminalPromptFrameController.cs")), StringComparison.Ordinal);
        Assert.True(
            tickSource.IndexOf("assistantTail.HasUncommittedText()", StringComparison.Ordinal)
            < tickSource.IndexOf("shouldSkipWorkingDockRefresh()", StringComparison.Ordinal));
        Assert.True(
            tickSource.IndexOf("shouldSkipWorkingDockRefresh()", StringComparison.Ordinal)
            < tickSource.IndexOf("RefreshAndRestoreInlineTailPromptUnsafe()", StringComparison.Ordinal));
    }

    private static string[] SplitBannerLines(string banner)
        => banner.Split(Environment.NewLine, StringSplitOptions.None);

    [Fact]
    public void CliRuntimeCommandRunner_ThreadResumeReplayStatePendingFields_WhenInternalized_UseCliLocalTypes()
    {
        var replayStateType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner+ThreadResumeReplayState");
        var approvalStateType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliPendingApprovalRequestState");
        var permissionStateType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliPendingPermissionRequestState");
        var userInputStateType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliPendingUserInputRequestState");

        var pendingApprovalsField = replayStateType.GetField("pendingApprovals", BindingFlags.Instance | BindingFlags.NonPublic);
        var pendingPermissionField = replayStateType.GetField("pendingPermissionRequests", BindingFlags.Instance | BindingFlags.NonPublic);
        var pendingUserInputField = replayStateType.GetField("pendingUserInputs", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(pendingApprovalsField);
        Assert.NotNull(pendingPermissionField);
        Assert.NotNull(pendingUserInputField);

        Assert.Equal(
            typeof(ConcurrentDictionary<string, object>).GetGenericTypeDefinition(),
            pendingApprovalsField!.FieldType.GetGenericTypeDefinition());
        Assert.Equal(typeof(string), pendingApprovalsField.FieldType.GetGenericArguments()[0]);
        Assert.Equal(approvalStateType, pendingApprovalsField.FieldType.GetGenericArguments()[1]);
        Assert.Equal(permissionStateType, pendingPermissionField!.FieldType.GetGenericArguments()[1]);
        Assert.Equal(userInputStateType, pendingUserInputField!.FieldType.GetGenericArguments()[1]);
    }

    [Fact]
    public void CliApprovalResponseResolver_WhenApprovalOptionsAreInternalized_UsesCliLocalTypes()
    {
        var resolverType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliApprovalResponseResolver");
        var optionType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliApprovalDecisionOptionPayload");
        var pendingApprovalStateType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliPendingApprovalRequestState");

        var resolveDecisionOption = resolverType.GetMethod("ResolveDecisionOption", BindingFlags.Static | BindingFlags.NonPublic);
        var buildDecisionOptionsFromDecisionTokens = resolverType.GetMethod(
            "BuildDecisionOptionsFromDecisionTokens",
            BindingFlags.Static | BindingFlags.NonPublic);
        var buildResolutionOverloads = resolverType
            .GetMethods(BindingFlags.Static | BindingFlags.Public)
            .Where(static method => string.Equals(method.Name, "BuildResolution", StringComparison.Ordinal))
            .ToArray();

        Assert.NotNull(resolveDecisionOption);
        Assert.NotNull(buildDecisionOptionsFromDecisionTokens);
        Assert.Equal(optionType, resolveDecisionOption!.ReturnType);
        Assert.Equal(optionType, buildDecisionOptionsFromDecisionTokens!.ReturnType.GetGenericArguments()[0]);
        Assert.Single(buildResolutionOverloads);
        Assert.Equal(pendingApprovalStateType, buildResolutionOverloads[0].GetParameters()[1].ParameterType);
    }

    [Fact]
    public void CliInteractiveStateConverters_ToPendingApprovalRequestState_ProjectsCliLocalDecisionOptions()
    {
        var compatibilityType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliInteractiveStateConverters");

        var streamEvent = ControlPlaneConversationStreamEventCompatibility.ToControlPlaneConversationStreamEvent(
            new AgentStreamEvent
            {
                Kind = AgentStreamEventKind.ApprovalRequested,
                CallId = "call-cli-pending-1",
                ThreadId = "thread-cli-pending-1",
                TurnId = "turn-cli-pending-1",
                ToolName = "shell",
                ApprovalKind = "command",
                AvailableDecisions = ["acceptWithExecpolicyAmendment", "decline"],
                AvailableDecisionOptions =
                [
                    new ApprovalDecisionOptionPayload(
                        "acceptWithExecpolicyAmendment",
                        new ExecPolicyAmendmentPayload(["git", "status"]))
                ],
            });
        var pendingState = ReflectionTestHelper.InvokeStaticMethod(
            compatibilityType,
            "ToPendingApprovalRequestState",
            streamEvent);

        Assert.NotNull(pendingState);
        Assert.Equal("CliPendingApprovalRequestState", pendingState!.GetType().Name);
        Assert.Equal("call-cli-pending-1", ReflectionTestHelper.GetProperty(pendingState, "CallId"));

        var options = Assert.IsAssignableFrom<IEnumerable>(ReflectionTestHelper.GetProperty(pendingState, "AvailableDecisionOptions")!)
            .Cast<object>()
            .ToArray();
        var option = Assert.Single(options);
        Assert.Equal("CliApprovalDecisionOptionPayload", option.GetType().Name);
        Assert.Equal("acceptWithExecpolicyAmendment", ReflectionTestHelper.GetProperty(option, "Type"));

        var execPolicy = ReflectionTestHelper.GetProperty(option, "ExecPolicyAmendment");
        Assert.NotNull(execPolicy);
        var commandPrefix = Assert.IsAssignableFrom<IEnumerable>(ReflectionTestHelper.GetProperty(execPolicy!, "CommandPrefix")!)
            .Cast<string>()
            .ToArray();
        Assert.Equal(["git", "status"], commandPrefix);
    }

    [Fact]
    public void CliInteractiveStateConverters_TypedOverloads_ProjectControlPlaneStreamEnvelope()
    {
        var compatibilityType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliInteractiveStateConverters");

        var approvalState = ReflectionTestHelper.InvokeStaticMethod(
            compatibilityType,
            "ToPendingApprovalRequestState",
            new ControlPlaneConversationStreamEvent
            {
                Kind = ControlPlaneConversationStreamEventKind.ApprovalRequested,
                CallId = new CallId("call-typed-approval-1"),
                ThreadId = new ThreadId("thread-typed-approval-1"),
                TurnId = new TurnId("turn-typed-approval-1"),
                ToolName = "shell_command",
                ApprovalKind = "command_execution",
                AvailableDecisions = ["accept", "decline"],
                AvailableDecisionOptions =
                [
                    new ControlPlaneApprovalDecisionOption(
                        "accept",
                        new ControlPlaneExecPolicyAmendment(["git", "status"]))
                ],
            });

        Assert.NotNull(approvalState);
        Assert.Equal("call-typed-approval-1", ReflectionTestHelper.GetProperty(approvalState!, "CallId"));
        Assert.Equal("thread-typed-approval-1", ReflectionTestHelper.GetProperty(approvalState, "ThreadId"));
        Assert.Equal("turn-typed-approval-1", ReflectionTestHelper.GetProperty(approvalState, "TurnId"));
        var typedOptions = Assert.IsAssignableFrom<IEnumerable>(ReflectionTestHelper.GetProperty(approvalState, "AvailableDecisionOptions")!)
            .Cast<object>()
            .ToArray();
        Assert.Single(typedOptions);

        var permissionState = ReflectionTestHelper.InvokeStaticMethod(
            compatibilityType,
            "ToPendingPermissionRequestState",
            new ControlPlaneConversationStreamEvent
            {
                Kind = ControlPlaneConversationStreamEventKind.PermissionRequested,
                CallId = new CallId("call-typed-permission-1"),
                ThreadId = new ThreadId("thread-typed-permission-1"),
                TurnId = new TurnId("turn-typed-permission-1"),
                ToolName = "shell_command",
            });

        Assert.NotNull(permissionState);
        Assert.Equal("call-typed-permission-1", ReflectionTestHelper.GetProperty(permissionState!, "CallId"));
        Assert.Equal("shell_command", ReflectionTestHelper.GetProperty(permissionState, "ToolName"));

        var userInputState = ReflectionTestHelper.InvokeStaticMethod(
            compatibilityType,
            "ToPendingUserInputRequestState",
            new ControlPlaneConversationStreamEvent
            {
                Kind = ControlPlaneConversationStreamEventKind.UserInputRequested,
                CallId = new CallId("call-typed-user-input-1"),
                ThreadId = new ThreadId("thread-typed-user-input-1"),
                TurnId = new TurnId("turn-typed-user-input-1"),
                ToolName = "prompt_input",
            });

        Assert.NotNull(userInputState);
        Assert.Equal("call-typed-user-input-1", ReflectionTestHelper.GetProperty(userInputState!, "CallId"));
        Assert.Equal("prompt_input", ReflectionTestHelper.GetProperty(userInputState, "ToolName"));
    }

    [Fact]
    public void BuildStructuredUserInputsFromText_WithLinkedMentionSyntax_ProducesStructuredInputs()
    {
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.InteractiveChatRunner");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            runnerType,
            "BuildStructuredUserInputsFromText",
            "Use [$figma](app://figma-1), [@sample](plugin://sample@test), and [$skill](C:/skills/demo/SKILL.md).");

        var inputs = Assert.IsAssignableFrom<IEnumerable>(result).Cast<object>().ToArray();
        Assert.Equal(4, inputs.Length);

        Assert.Equal("ControlPlaneTextInput", inputs[0].GetType().Name);
        Assert.Equal("Use $figma, $sample, and $skill.", ReflectionTestHelper.GetProperty(inputs[0], "Text"));

        Assert.Equal("ControlPlaneMentionInput", inputs[1].GetType().Name);
        Assert.Equal("figma", ReflectionTestHelper.GetProperty(inputs[1], "Name"));
        Assert.Equal("app://figma-1", ReflectionTestHelper.GetProperty(inputs[1], "Path"));

        Assert.Equal("ControlPlaneMentionInput", inputs[2].GetType().Name);
        Assert.Equal("sample", ReflectionTestHelper.GetProperty(inputs[2], "Name"));
        Assert.Equal("plugin://sample@test", ReflectionTestHelper.GetProperty(inputs[2], "Path"));

        Assert.Equal("ControlPlaneSkillInput", inputs[3].GetType().Name);
        Assert.Equal("skill", ReflectionTestHelper.GetProperty(inputs[3], "Name"));
        Assert.Equal("C:/skills/demo/SKILL.md", ReflectionTestHelper.GetProperty(inputs[3], "Path"));
    }

    [Fact]
    public void BuildStructuredUserInputsFromText_DeduplicatesLinkedTargets()
    {
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.InteractiveChatRunner");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            runnerType,
            "BuildStructuredUserInputsFromText",
            "Repeat [$figma](app://figma-1) and [$figma](app://figma-1) plus [$skill](skill://C:/skills/demo/SKILL.md) and [$skill](skill://C:/skills/demo/SKILL.md).");

        var inputs = Assert.IsAssignableFrom<IEnumerable>(result).Cast<object>().ToArray();
        Assert.Equal(3, inputs.Length);

        Assert.Equal("ControlPlaneTextInput", inputs[0].GetType().Name);
        Assert.Equal(
            "Repeat $figma and $figma plus $skill and $skill.",
            ReflectionTestHelper.GetProperty(inputs[0], "Text"));

        Assert.Equal("ControlPlaneMentionInput", inputs[1].GetType().Name);
        Assert.Equal("app://figma-1", ReflectionTestHelper.GetProperty(inputs[1], "Path"));

        Assert.Equal("ControlPlaneSkillInput", inputs[2].GetType().Name);
        Assert.Equal("C:/skills/demo/SKILL.md", ReflectionTestHelper.GetProperty(inputs[2], "Path"));
    }

    [Fact]
    public void Parse_ModelListCommand_SetsLimit_AndIncludeHidden()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "model-route", "list", "--limit", "5", "--include-hidden", "--json" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("RuntimeSurfaceCommandOptions", command!.GetType().Name);
        Assert.Equal("ModelList", ReflectionTestHelper.GetProperty(command, "CommandKind")?.ToString());
        Assert.Equal(5, ReflectionTestHelper.GetProperty(command, "Limit"));
        Assert.Equal(true, ReflectionTestHelper.GetProperty(command, "IncludeHidden"));
        Assert.Equal(true, ReflectionTestHelper.GetProperty(command, "OutputJson"));
    }

    [Fact]
    public void Parse_ModelRootCommand_DoesNotResolveRuntimeSurfaceCommand()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "model", "list", "--json" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.Null(command);
        Assert.Equal(true, ReflectionTestHelper.GetProperty(result!, "ShowHelp"));
        var errorMessage = Assert.IsType<string>(ReflectionTestHelper.GetProperty(result!, "ErrorMessage"));
        Assert.False(string.IsNullOrWhiteSpace(errorMessage));
    }

    [Fact]
    public void Parse_ToolsListCommand_SetsRuntimeSurfaceKindAndIncludeHidden()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "tools", "list", "--include-hidden", "--json" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("RuntimeSurfaceCommandOptions", command!.GetType().Name);
        Assert.Equal("ToolCatalog", ReflectionTestHelper.GetProperty(command, "CommandKind")?.ToString());
        Assert.Equal(true, ReflectionTestHelper.GetProperty(command, "IncludeHidden"));
        Assert.Equal(true, ReflectionTestHelper.GetProperty(command, "OutputJson"));
    }

    [Fact]
    public void Parse_ToolsExportConfigCommand_SetsOutputPathAndIncludesHidden()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "tools", "export-config", "--out", "D:/Exports/tool_profiles.builtin.toml", "--json" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("RuntimeSurfaceCommandOptions", command!.GetType().Name);
        Assert.Equal("ToolConfigExport", ReflectionTestHelper.GetProperty(command, "CommandKind")?.ToString());
        Assert.Equal(true, ReflectionTestHelper.GetProperty(command, "IncludeHidden"));
        var outputPath = Assert.IsType<string>(ReflectionTestHelper.GetProperty(command, "ToolConfigOutputPath"));
        Assert.EndsWith("tool_profiles.builtin.toml", outputPath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(true, ReflectionTestHelper.GetProperty(command, "OutputJson"));
    }

    [Fact]
    public void Parse_ModelCatalogResolveAndAgentListCommands_SetExpectedRuntimeSurfaceKinds()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");

        var catalogResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "model-route", "catalog", "--limit", "8", "--include-hidden", "--json" });
        var catalogCommand = ReflectionTestHelper.GetProperty(catalogResult!, "Command");
        Assert.NotNull(catalogCommand);
        Assert.Equal("RuntimeSurfaceCommandOptions", catalogCommand!.GetType().Name);
        Assert.Equal("ModelCatalog", ReflectionTestHelper.GetProperty(catalogCommand, "CommandKind")?.ToString());
        Assert.Equal(8, ReflectionTestHelper.GetProperty(catalogCommand, "Limit"));
        Assert.Equal(true, ReflectionTestHelper.GetProperty(catalogCommand, "IncludeHidden"));

        var resolveResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[]
            {
                "model-route",
                "resolve",
                "--provider-key",
                "openai",
                "--model-key",
                "gpt-5",
                "--reasoning-effort",
                "high",
                "--reasoning-summary",
                "detailed",
                "--verbosity",
                "verbose",
                "--prefer-websocket-transport",
            });
        var resolveCommand = ReflectionTestHelper.GetProperty(resolveResult!, "Command");
        Assert.NotNull(resolveCommand);
        Assert.Equal("ModelResolve", ReflectionTestHelper.GetProperty(resolveCommand!, "CommandKind")?.ToString());
        Assert.Equal("openai", ReflectionTestHelper.GetProperty(resolveCommand, "ProviderKey"));
        Assert.Equal("gpt-5", ReflectionTestHelper.GetProperty(resolveCommand, "ModelKey"));
        Assert.Equal("high", ReflectionTestHelper.GetProperty(resolveCommand, "ReasoningEffort"));
        Assert.Equal("detailed", ReflectionTestHelper.GetProperty(resolveCommand, "ReasoningSummary"));
        Assert.Equal("verbose", ReflectionTestHelper.GetProperty(resolveCommand, "Verbosity"));
        Assert.Equal(true, ReflectionTestHelper.GetProperty(resolveCommand, "PreferWebsocketTransport"));

        var agentListResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "agent", "list", "--limit", "6", "--cursor", "cursor-agent-001", "--include-primary-threads" });
        var agentListCommand = ReflectionTestHelper.GetProperty(agentListResult!, "Command");
        Assert.NotNull(agentListCommand);
        Assert.Equal("AgentList", ReflectionTestHelper.GetProperty(agentListCommand!, "CommandKind")?.ToString());
        Assert.Equal(6, ReflectionTestHelper.GetProperty(agentListCommand, "Limit"));
        Assert.Equal("cursor-agent-001", ReflectionTestHelper.GetProperty(agentListCommand, "Cursor"));
        Assert.Equal(true, ReflectionTestHelper.GetProperty(agentListCommand, "IncludePrimaryThreads"));
    }

    [Fact]
    public void Parse_ModelRouteCommand_SetsLocalDiagnosticOptions()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "model-route", "route", "--route", "coding", "--route-set", "beta", "--json" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("ModelRouteDiagnosticCommandOptions", command!.GetType().Name);
        Assert.Equal("coding", ReflectionTestHelper.GetProperty(command, "RouteKind"));
        Assert.Equal("beta", ReflectionTestHelper.GetProperty(command, "RouteSetId"));
        Assert.Equal(true, ReflectionTestHelper.GetProperty(command, "OutputJson"));
        Assert.Equal(false, ReflectionTestHelper.GetProperty(command, "CreateThreadOnInitialize"));
    }

    [Fact]
    public void Parse_SessionAndWorkflowFormalQueryCommands_SetExpectedRuntimeSurfaceKinds()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");

        var sessionSnapshotResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "session", "snapshot", "--json" });
        var sessionSnapshotCommand = ReflectionTestHelper.GetProperty(sessionSnapshotResult!, "Command");
        Assert.NotNull(sessionSnapshotCommand);
        Assert.Equal("RuntimeSurfaceCommandOptions", sessionSnapshotCommand!.GetType().Name);
        Assert.Equal("SessionSnapshot", ReflectionTestHelper.GetProperty(sessionSnapshotCommand, "CommandKind")?.ToString());

        var sessionResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "session", "overview", "--session-id", "session-expansion-001", "--json" });
        var sessionCommand = ReflectionTestHelper.GetProperty(sessionResult!, "Command");
        Assert.NotNull(sessionCommand);
        Assert.Equal("RuntimeSurfaceCommandOptions", sessionCommand!.GetType().Name);
        Assert.Equal("SessionOverview", ReflectionTestHelper.GetProperty(sessionCommand, "CommandKind")?.ToString());
        Assert.Equal("session-expansion-001", ReflectionTestHelper.GetProperty(sessionCommand, "SessionId"));

        var workflowResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "workflow", "taskboard", "--workflow-id", "workflow-expansion-001" });
        var workflowCommand = ReflectionTestHelper.GetProperty(workflowResult!, "Command");
        Assert.NotNull(workflowCommand);
        Assert.Equal("WorkflowTaskBoard", ReflectionTestHelper.GetProperty(workflowCommand!, "CommandKind")?.ToString());
        Assert.Equal("workflow-expansion-001", ReflectionTestHelper.GetProperty(workflowCommand, "WorkflowId"));

        var agentResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "agent", "team", "--team-id", "team-expansion-001" });
        var agentCommand = ReflectionTestHelper.GetProperty(agentResult!, "Command");
        Assert.NotNull(agentCommand);
        Assert.Equal("AgentTeam", ReflectionTestHelper.GetProperty(agentCommand!, "CommandKind")?.ToString());
        Assert.Equal("team-expansion-001", ReflectionTestHelper.GetProperty(agentCommand, "TeamId"));
    }

    [Fact]
    public void Parse_CollaborationAndParticipantFormalQueryCommands_SetExpectedRuntimeSurfaceKinds()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");

        var collaborationResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "collaboration", "read", "--space-id", "space-expansion-002", "--json" });
        var collaborationCommand = ReflectionTestHelper.GetProperty(collaborationResult!, "Command");
        Assert.NotNull(collaborationCommand);
        Assert.Equal("RuntimeSurfaceCommandOptions", collaborationCommand!.GetType().Name);
        Assert.Equal("CollaborationSpace", ReflectionTestHelper.GetProperty(collaborationCommand, "CommandKind")?.ToString());
        Assert.Equal("space-expansion-002", ReflectionTestHelper.GetProperty(collaborationCommand, "CollaborationSpaceId"));

        var participantResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "participant", "view", "--participant-id", "participant-expansion-001" });
        var participantCommand = ReflectionTestHelper.GetProperty(participantResult!, "Command");
        Assert.NotNull(participantCommand);
        Assert.Equal("ParticipantView", ReflectionTestHelper.GetProperty(participantCommand!, "CommandKind")?.ToString());
        Assert.Equal("participant-expansion-001", ReflectionTestHelper.GetProperty(participantCommand, "ParticipantId"));
    }

    [Fact]
    public void Parse_CollaborationFormalCommands_SetExpectedRuntimeSurfaceKinds()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");

        var createResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[]
            {
                "collaboration",
                "create",
                "--space-id",
                "space-create-001",
                "--key",
                "team-alpha",
                "--display-name",
                "Team Alpha",
                "--purpose",
                "Cross repo collaboration",
                "--default-workspace",
                "D:/Repos/TianShu",
                "--default-execution-profile",
                "review",
                "--policy-key",
                "policy-alpha",
            });
        var createCommand = ReflectionTestHelper.GetProperty(createResult!, "Command");
        Assert.NotNull(createCommand);
        Assert.Equal("CollaborationCreate", ReflectionTestHelper.GetProperty(createCommand!, "CommandKind")?.ToString());
        Assert.Equal("space-create-001", ReflectionTestHelper.GetProperty(createCommand, "CollaborationSpaceId"));
        Assert.Equal("team-alpha", ReflectionTestHelper.GetProperty(createCommand, "CollaborationSpaceKey"));
        Assert.Equal("Team Alpha", ReflectionTestHelper.GetProperty(createCommand, "DisplayName"));
        Assert.Equal("Cross repo collaboration", ReflectionTestHelper.GetProperty(createCommand, "Purpose"));
        Assert.Equal(Path.GetFullPath("D:/Repos/TianShu"), ReflectionTestHelper.GetProperty(createCommand, "DefaultWorkspace"));
        Assert.Equal("review", ReflectionTestHelper.GetProperty(createCommand, "DefaultExecutionProfile"));
        Assert.Equal("policy-alpha", ReflectionTestHelper.GetProperty(createCommand, "PolicyKey"));

        var bindSessionResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "participant", "bind-session", "--participant-id", "participant-001", "--session-id", "session-001" });
        var bindSessionCommand = ReflectionTestHelper.GetProperty(bindSessionResult!, "Command");
        Assert.NotNull(bindSessionCommand);
        Assert.Equal("ParticipantBindSession", ReflectionTestHelper.GetProperty(bindSessionCommand!, "CommandKind")?.ToString());
        Assert.Equal("participant-001", ReflectionTestHelper.GetProperty(bindSessionCommand, "ParticipantId"));
        Assert.Equal("session-001", ReflectionTestHelper.GetProperty(bindSessionCommand, "SessionId"));

        var bindWorkflowResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "participant", "bind-workflow", "--participant-id", "participant-002", "--workflow-id", "workflow-002" });
        var bindWorkflowCommand = ReflectionTestHelper.GetProperty(bindWorkflowResult!, "Command");
        Assert.NotNull(bindWorkflowCommand);
        Assert.Equal("ParticipantBindWorkflow", ReflectionTestHelper.GetProperty(bindWorkflowCommand!, "CommandKind")?.ToString());
        Assert.Equal("workflow-002", ReflectionTestHelper.GetProperty(bindWorkflowCommand, "WorkflowId"));

        var updateRoleResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "participant", "update-role", "--participant-id", "participant-003", "--role", "owner" });
        var updateRoleCommand = ReflectionTestHelper.GetProperty(updateRoleResult!, "Command");
        Assert.NotNull(updateRoleCommand);
        Assert.Equal("ParticipantUpdateRole", ReflectionTestHelper.GetProperty(updateRoleCommand!, "CommandKind")?.ToString());
        Assert.Equal("owner", ReflectionTestHelper.GetProperty(updateRoleCommand, "Role"));
    }

    [Fact]
    public void Parse_ConversationGovernanceAndArtifactFormalQueryCommands_SetExpectedRuntimeSurfaceKinds()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");

        var conversationResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "conversation", "read", "--thread-id", "thread-expansion-001", "--json" });
        var conversationCommand = ReflectionTestHelper.GetProperty(conversationResult!, "Command");
        Assert.NotNull(conversationCommand);
        Assert.Equal("RuntimeSurfaceCommandOptions", conversationCommand!.GetType().Name);
        Assert.Equal("ConversationThread", ReflectionTestHelper.GetProperty(conversationCommand, "CommandKind")?.ToString());
        Assert.Equal("thread-expansion-001", ReflectionTestHelper.GetProperty(conversationCommand, "ThreadId"));

        var governanceResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "governance", "approvals", "--participant-id", "participant-expansion-003" });
        var governanceCommand = ReflectionTestHelper.GetProperty(governanceResult!, "Command");
        Assert.NotNull(governanceCommand);
        Assert.Equal("GovernanceApprovalQueue", ReflectionTestHelper.GetProperty(governanceCommand!, "CommandKind")?.ToString());
        Assert.Equal("participant-expansion-003", ReflectionTestHelper.GetProperty(governanceCommand, "ParticipantId"));

        var artifactResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "artifact", "read", "--artifact-id", "artifact-expansion-001" });
        var artifactCommand = ReflectionTestHelper.GetProperty(artifactResult!, "Command");
        Assert.NotNull(artifactCommand);
        Assert.Equal("ArtifactRead", ReflectionTestHelper.GetProperty(artifactCommand!, "CommandKind")?.ToString());
        Assert.Equal("artifact-expansion-001", ReflectionTestHelper.GetProperty(artifactCommand, "ArtifactId"));
    }

    [Fact]
    public void Parse_DiagnosticsFormalQueryCommands_SetExpectedRuntimeSurfaceKinds()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");

        var traceResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "diagnostics", "trace", "--trace-id", "trace-expansion-001", "--json" });
        var traceCommand = ReflectionTestHelper.GetProperty(traceResult!, "Command");
        Assert.NotNull(traceCommand);
        Assert.Equal("RuntimeSurfaceCommandOptions", traceCommand!.GetType().Name);
        Assert.Equal("DiagnosticsTrace", ReflectionTestHelper.GetProperty(traceCommand, "CommandKind")?.ToString());
        Assert.Equal("trace-expansion-001", ReflectionTestHelper.GetProperty(traceCommand, "TraceId"));

        var attemptsResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "diagnostics", "attempts", "--execution-id", "execution-expansion-001" });
        var attemptsCommand = ReflectionTestHelper.GetProperty(attemptsResult!, "Command");
        Assert.NotNull(attemptsCommand);
        Assert.Equal("DiagnosticsAttemptList", ReflectionTestHelper.GetProperty(attemptsCommand!, "CommandKind")?.ToString());
        Assert.Equal("execution-expansion-001", ReflectionTestHelper.GetProperty(attemptsCommand, "ExecutionId"));
    }

    [Fact]
    public void Parse_IdentityAndMemoryFormalQueryCommands_SetExpectedRuntimeSurfaceKinds()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");

        var accountResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "identity", "account", "--account-id", "account-expansion-001", "--json" });
        var accountCommand = ReflectionTestHelper.GetProperty(accountResult!, "Command");
        Assert.NotNull(accountCommand);
        Assert.Equal("RuntimeSurfaceCommandOptions", accountCommand!.GetType().Name);
        Assert.Equal("IdentityAccount", ReflectionTestHelper.GetProperty(accountCommand, "CommandKind")?.ToString());
        Assert.Equal("account-expansion-001", ReflectionTestHelper.GetProperty(accountCommand, "AccountId"));

        var devicesResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "identity", "devices", "--account-id", "account-expansion-002" });
        var devicesCommand = ReflectionTestHelper.GetProperty(devicesResult!, "Command");
        Assert.NotNull(devicesCommand);
        Assert.Equal("IdentityDevices", ReflectionTestHelper.GetProperty(devicesCommand!, "CommandKind")?.ToString());
        Assert.Equal("account-expansion-002", ReflectionTestHelper.GetProperty(devicesCommand, "AccountId"));

        var spacesResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "memory", "spaces", "--scope-kind", "workspace" });
        var spacesCommand = ReflectionTestHelper.GetProperty(spacesResult!, "Command");
        Assert.NotNull(spacesCommand);
        Assert.Equal("MemorySpaces", ReflectionTestHelper.GetProperty(spacesCommand!, "CommandKind")?.ToString());
        Assert.Equal("Workspace", ReflectionTestHelper.GetProperty(spacesCommand, "MemoryScopeKind")?.ToString());

        var overlayResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "memory", "overlay", "--memory-space-id", "memory-space-expansion-001", "--space-id", "space-expansion-005" });
        var overlayCommand = ReflectionTestHelper.GetProperty(overlayResult!, "Command");
        Assert.NotNull(overlayCommand);
        Assert.Equal("MemoryOverlay", ReflectionTestHelper.GetProperty(overlayCommand!, "CommandKind")?.ToString());
        Assert.Equal("memory-space-expansion-001", ReflectionTestHelper.GetProperty(overlayCommand, "MemorySpaceId"));
        Assert.Equal("space-expansion-005", ReflectionTestHelper.GetProperty(overlayCommand, "CollaborationSpaceId"));
    }

    [Fact]
    public void Parse_PluginInstallCommand_SetsMarketplacePath_And_PluginName()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "plugin", "install", "--marketplace-path", "D:\\marketplace", "--plugin-name", "demo-plugin" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("RuntimeSurfaceCommandOptions", command!.GetType().Name);
        Assert.Equal("PluginInstall", ReflectionTestHelper.GetProperty(command, "CommandKind")?.ToString());
        Assert.Equal(Path.GetFullPath("D:\\marketplace"), ReflectionTestHelper.GetProperty(command, "MarketplacePath"));
        Assert.Equal("demo-plugin", ReflectionTestHelper.GetProperty(command, "PluginName"));
    }

    [Fact]
    public void Parse_PluginReadCommand_SetsMarketplacePath_And_PluginName()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "plugin", "read", "--marketplace-path", "D:\\marketplace", "--plugin-name", "demo-plugin" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("RuntimeSurfaceCommandOptions", command!.GetType().Name);
        Assert.Equal("PluginRead", ReflectionTestHelper.GetProperty(command, "CommandKind")?.ToString());
        Assert.Equal(Path.GetFullPath("D:\\marketplace"), ReflectionTestHelper.GetProperty(command, "MarketplacePath"));
        Assert.Equal("demo-plugin", ReflectionTestHelper.GetProperty(command, "PluginName"));
    }

    [Fact]
    public void Parse_PluginListCommand_SetsKind()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "plugin", "list", "--json" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("RuntimeSurfaceCommandOptions", command!.GetType().Name);
        Assert.Equal("PluginList", ReflectionTestHelper.GetProperty(command, "CommandKind")?.ToString());
        Assert.Equal(true, ReflectionTestHelper.GetProperty(command, "OutputJson"));
    }

    [Fact]
    public void Parse_FeaturesEnableCommand_SetsFeatureName_AndEnabled()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "features", "enable", "unified_exec", "--json" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("RuntimeSurfaceCommandOptions", command!.GetType().Name);
        Assert.Equal("FeatureConfigWrite", ReflectionTestHelper.GetProperty(command, "CommandKind")?.ToString());
        Assert.Equal("unified_exec", ReflectionTestHelper.GetProperty(command, "FeatureName"));
        Assert.Equal(true, ReflectionTestHelper.GetProperty(command, "Enabled"));
        Assert.Equal(true, ReflectionTestHelper.GetProperty(command, "OutputJson"));
    }

    [Fact]
    public void Parse_FeaturesEnableWithoutFeatureName_ReturnsErrorMessage()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "features", "enable" });
        Assert.NotNull(result);

        Assert.Null(ReflectionTestHelper.GetProperty(result!, "Command"));
        var errorMessage = Assert.IsType<string>(ReflectionTestHelper.GetProperty(result!, "ErrorMessage"));
        Assert.Contains("缺少必填参数：<feature>", errorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_CompletionCommand_DefaultsToBash()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "completion" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("CompletionCommandOptions", command!.GetType().Name);
        Assert.Equal("Bash", ReflectionTestHelper.GetProperty(command, "Shell")?.ToString());
    }

    [Fact]
    public void Parse_CompletionCommand_AcceptsPowerShellAlias()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "completion", "pwsh" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("CompletionCommandOptions", command!.GetType().Name);
        Assert.Equal("PowerShell", ReflectionTestHelper.GetProperty(command, "Shell")?.ToString());
    }

    [Fact]
    public void GenerateCompletionScript_Bash_IncludesRootCommands_AndShellChoices()
    {
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CompletionCommandRunner");
        var optionsType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CompletionCommandOptions");
        var shellType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CompletionShellKind");

        var options = Activator.CreateInstance(optionsType);
        Assert.NotNull(options);
        ReflectionTestHelper.SetProperty(options!, "Shell", Enum.Parse(shellType, "Bash"));

        var script = Assert.IsType<string>(ReflectionTestHelper.InvokeStaticMethod(runnerType, "GenerateScript", options!));
        Assert.Contains("_tianshu_completion", script, StringComparison.Ordinal);
        Assert.Contains("completion send follow-up chat", script, StringComparison.Ordinal);
        Assert.Contains("debug", script, StringComparison.Ordinal);
        Assert.Contains("clear-memories", script, StringComparison.Ordinal);
        Assert.Contains("bash zsh fish powershell", script, StringComparison.Ordinal);
        Assert.Contains("completion_command=\"tianshu\"", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_DebugClearMemoriesCommand_SetsKind_And_CommonOptions()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        using var tempDir = new TestTempDirectory();
        var appHostProjectPath = Path.Combine(tempDir.Path, "TianShu.AppHost.csproj");
        var configFilePath = Path.Combine(tempDir.Path, "config.toml");
        File.WriteAllText(appHostProjectPath, "<Project />");
        File.WriteAllText(configFilePath, "model = 'gpt-5'");

        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[]
            {
                "debug",
                "clear-memories",
                "--json",
                "--cwd",
                tempDir.Path,
                "--apphost-project",
                appHostProjectPath,
                "--config-file",
                configFilePath,
                "--profile",
                "work",
            });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("DebugCommandOptions", command!.GetType().Name);
        Assert.Equal("ClearMemories", ReflectionTestHelper.GetProperty(command, "CommandKind")?.ToString());
        Assert.Equal(true, ReflectionTestHelper.GetProperty(command, "OutputJson"));
        Assert.Equal(tempDir.Path, ReflectionTestHelper.GetProperty(command, "WorkingDirectory"));
        Assert.Equal(appHostProjectPath, ReflectionTestHelper.GetProperty(command, "AppHostProjectPath"));
        Assert.Equal(configFilePath, ReflectionTestHelper.GetProperty(command, "ConfigFilePath"));
        Assert.Equal("work", ReflectionTestHelper.GetProperty(command, "ProfileName"));
        Assert.Equal(false, ReflectionTestHelper.GetProperty(command, "CreateThreadOnInitialize"));
    }

    [Fact]
    public void Parse_AppServerCommand_DefaultsToStdioListen_And_CollectsLaunchOptions()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        using var tempDir = new TestTempDirectory();
        var appHostProjectPath = Path.Combine(tempDir.Path, "TianShu.AppHost.csproj");
        var configFilePath = Path.Combine(tempDir.Path, "config.toml");
        File.WriteAllText(appHostProjectPath, "<Project />");
        File.WriteAllText(configFilePath, "model = 'gpt-5'");

        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[]
            {
                "app-server",
                "--cwd", tempDir.Path,
                "--apphost-project", appHostProjectPath,
                "--config-file", configFilePath,
                "-c", "sandbox_mode=workspace-write",
            });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("AppServerCommandOptions", command!.GetType().Name);
        Assert.Equal("RunServer", ReflectionTestHelper.GetProperty(command, "CommandKind")?.ToString());
        Assert.Equal("stdio://", ReflectionTestHelper.GetProperty(command, "ListenUrl"));
        Assert.Equal(tempDir.Path, ReflectionTestHelper.GetProperty(command, "WorkingDirectory"));
        Assert.Equal(appHostProjectPath, ReflectionTestHelper.GetProperty(command, "AppHostProjectPath"));
        Assert.Equal(configFilePath, ReflectionTestHelper.GetProperty(command, "ConfigFilePath"));

        var overrides = Assert.IsAssignableFrom<System.Collections.IDictionary>(ReflectionTestHelper.GetProperty(command, "ConfigOverrides"));
        Assert.Equal("workspace-write", overrides["sandbox_mode"]);
    }

    [Fact]
    public void Parse_AppServerCommand_AcceptsAnalyticsDefaultEnabledFlag()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "app-server", "--analytics-default-enabled" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("AppServerCommandOptions", command!.GetType().Name);
        Assert.Equal("RunServer", ReflectionTestHelper.GetProperty(command, "CommandKind")?.ToString());
        Assert.Equal(true, ReflectionTestHelper.GetProperty(command, "AnalyticsDefaultEnabled"));
    }

    [Fact]
    public void Parse_AppServerGenerateTsCommand_ParsesOutPrettierAndExperimental()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "app-server", "generate-ts", "--out", ".\\artifacts\\ts", "--prettier", ".\\node_modules\\.bin\\prettier", "--experimental" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("AppServerCommandOptions", command!.GetType().Name);
        Assert.Equal("GenerateTs", ReflectionTestHelper.GetProperty(command, "CommandKind")?.ToString());
        Assert.Equal(Path.GetFullPath(".\\artifacts\\ts"), ReflectionTestHelper.GetProperty(command, "OutDirectory"));
        Assert.Equal(Path.GetFullPath(".\\node_modules\\.bin\\prettier"), ReflectionTestHelper.GetProperty(command, "PrettierPath"));
        Assert.Equal(true, ReflectionTestHelper.GetProperty(command, "Experimental"));
    }

    [Fact]
    public void Parse_AppServerGenerateJsonSchemaCommand_ParsesOutDirectory()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "app-server", "generate-json-schema", "-o", ".\\artifacts\\json" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("AppServerCommandOptions", command!.GetType().Name);
        Assert.Equal("GenerateJsonSchema", ReflectionTestHelper.GetProperty(command, "CommandKind")?.ToString());
        Assert.Equal(Path.GetFullPath(".\\artifacts\\json"), ReflectionTestHelper.GetProperty(command, "OutDirectory"));
    }

    [Fact]
    public void Parse_ExecShortAlias_UsesExecCommand()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "e", "当前目录是？" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("ExecCommandOptions", command!.GetType().Name);
        Assert.Equal("当前目录是？", ReflectionTestHelper.GetProperty(command, "Prompt"));
    }

    [Fact]
    public void Parse_ChatFullAuto_UsesWorkspaceWrite_AndOnRequestApproval()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "chat", "--full-auto" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("ChatCommandOptions", command!.GetType().Name);
        Assert.Equal("workspace-write", ReflectionTestHelper.GetProperty(command, "RuntimeSandboxMode"));
        Assert.Equal("on-request", ReflectionTestHelper.GetProperty(command, "RuntimeApprovalPolicy")?.ToString());
    }

    [Fact]
    public void Parse_ChatRootFeatureOverrides_WriteConfigOverrides()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "chat", "--enable", "model_routing", "--disable", "legacy_ui" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("ChatCommandOptions", command!.GetType().Name);

        var overrides = Assert.IsAssignableFrom<System.Collections.IDictionary>(ReflectionTestHelper.GetProperty(command, "ConfigOverrides"));
        Assert.Equal("true", overrides["features.model_routing"]);
        Assert.Equal("false", overrides["features.legacy_ui"]);
    }

    [Fact]
    public void Parse_Resume_AllowsTargetAfterFlags()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "resume", "--all", "thread-123" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("ChatCommandOptions", command!.GetType().Name);
        Assert.Equal("Resume", ReflectionTestHelper.GetProperty(command, "StartupThreadAction")?.ToString());
        Assert.Equal("thread-123", ReflectionTestHelper.GetProperty(command, "StartupThreadTarget"));
        Assert.Equal(true, ReflectionTestHelper.GetProperty(command, "StartupThreadShowAll"));
        Assert.Null(ReflectionTestHelper.GetProperty(command, "InitialMessage"));
    }

    [Fact]
    public void Parse_Fork_PreservesPromptWhenTargetAppearsAfterOptions()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "fork", "--model", "gpt-5", "thread-123", "re-review" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("ChatCommandOptions", command!.GetType().Name);
        Assert.Equal("Fork", ReflectionTestHelper.GetProperty(command, "StartupThreadAction")?.ToString());
        Assert.Equal("thread-123", ReflectionTestHelper.GetProperty(command, "StartupThreadTarget"));
        Assert.Equal("gpt-5", ReflectionTestHelper.GetProperty(command, "RuntimeModel"));
        Assert.Equal("re-review", ReflectionTestHelper.GetProperty(command, "InitialMessage"));
    }

    [Fact]
    public void BuildAppServerLaunchSpec_UsesDotNetRun_WhenBuiltKernelExecutableMissing()
    {
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.AppServerCommandRunner");
        var optionsType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.AppServerCommandOptions");
        using var tempDir = new TestTempDirectory();
        var appHostProjectPath = Path.Combine(tempDir.Path, "TianShu.AppHost.csproj");
        File.WriteAllText(appHostProjectPath, "<Project />");

        var command = Activator.CreateInstance(optionsType);
        Assert.NotNull(command);
        ReflectionTestHelper.SetProperty(command!, "WorkingDirectory", tempDir.Path);
        ReflectionTestHelper.SetProperty(command!, "AppHostProjectPath", appHostProjectPath);
        ReflectionTestHelper.SetProperty(command!, "ListenUrl", "ws://127.0.0.1:4222");
        var defaultConfigPath = Assert.IsType<string>(ReflectionTestHelper.GetProperty(command!, "ConfigFilePath"));
        var overrides = Assert.IsAssignableFrom<System.Collections.IDictionary>(ReflectionTestHelper.GetProperty(command!, "ConfigOverrides"));
        overrides["sandbox_mode"] = "workspace-write";
        ReflectionTestHelper.SetProperty(command!, "AnalyticsDefaultEnabled", true);

        var launchSpec = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildLaunchSpec", command!);
        Assert.NotNull(launchSpec);
        Assert.Equal("dotnet", ReflectionTestHelper.GetProperty(launchSpec!, "ExecutablePath"));
        Assert.Equal(tempDir.Path, ReflectionTestHelper.GetProperty(launchSpec, "WorkingDirectory"));

        var arguments = Assert.IsAssignableFrom<IReadOnlyList<string>>(ReflectionTestHelper.GetProperty(launchSpec, "Arguments"));
        Assert.Equal(["run", "--project", appHostProjectPath, "--", "app-server", "--listen", "ws://127.0.0.1:4222", "--analytics-default-enabled", "--config-file", defaultConfigPath, "-c", "sandbox_mode=workspace-write"], arguments);
    }

    [Fact]
    public void BuildAppServerLaunchSpec_UsesBuiltKernelExecutable_WhenAvailable()
    {
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.AppServerCommandRunner");
        var optionsType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.AppServerCommandOptions");
        using var tempDir = new TestTempDirectory();
        var appHostProjectPath = Path.Combine(tempDir.Path, "TianShu.AppHost.csproj");
        var executablePath = Path.Combine(tempDir.Path, "bin", "Debug", "net10.0", "TianShu.AppHost.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(executablePath)!);
        File.WriteAllText(appHostProjectPath, "<Project />");
        File.WriteAllText(executablePath, "stub");

        var command = Activator.CreateInstance(optionsType);
        Assert.NotNull(command);
        ReflectionTestHelper.SetProperty(command!, "WorkingDirectory", tempDir.Path);
        ReflectionTestHelper.SetProperty(command!, "AppHostProjectPath", appHostProjectPath);
        ReflectionTestHelper.SetProperty(command!, "ListenUrl", "stdio://");
        var defaultConfigPath = Assert.IsType<string>(ReflectionTestHelper.GetProperty(command!, "ConfigFilePath"));

        var launchSpec = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildLaunchSpec", command!);
        Assert.NotNull(launchSpec);
        Assert.Equal(executablePath, ReflectionTestHelper.GetProperty(launchSpec!, "ExecutablePath"));

        var arguments = Assert.IsAssignableFrom<IReadOnlyList<string>>(ReflectionTestHelper.GetProperty(launchSpec, "Arguments"));
        Assert.Equal(["app-server", "--listen", "stdio://", "--config-file", defaultConfigPath], arguments);
    }

    [Fact]
    public void BuildAppServerLaunchSpec_UsesPublishedKernelExecutable_WhenProjectMissing()
    {
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.AppServerCommandRunner");
        var optionsType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.AppServerCommandOptions");
        using var tempDir = new TestTempDirectory();
        var executablePath = Path.Combine(tempDir.Path, "TianShu.AppHost.exe");
        File.WriteAllText(executablePath, "stub");

        var command = Activator.CreateInstance(optionsType);
        Assert.NotNull(command);
        ReflectionTestHelper.SetProperty(command!, "WorkingDirectory", tempDir.Path);
        ReflectionTestHelper.SetProperty(command!, "ListenUrl", "stdio://");
        var defaultConfigPath = Assert.IsType<string>(ReflectionTestHelper.GetProperty(command!, "ConfigFilePath"));

        var launchSpec = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildLaunchSpec", command!);
        Assert.NotNull(launchSpec);
        Assert.Equal(executablePath, ReflectionTestHelper.GetProperty(launchSpec!, "ExecutablePath"));

        var arguments = Assert.IsAssignableFrom<IReadOnlyList<string>>(ReflectionTestHelper.GetProperty(launchSpec, "Arguments"));
        Assert.Equal(["app-server", "--listen", "stdio://", "--config-file", defaultConfigPath], arguments);
    }

    [Fact]
    public void BuildAppServerLaunchSpec_GenerateTs_UsesSubcommandArguments()
    {
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.AppServerCommandRunner");
        var optionsType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.AppServerCommandOptions");
        using var tempDir = new TestTempDirectory();
        var appHostProjectPath = Path.Combine(tempDir.Path, "TianShu.AppHost.csproj");
        File.WriteAllText(appHostProjectPath, "<Project />");

        var command = Activator.CreateInstance(optionsType);
        Assert.NotNull(command);
        ReflectionTestHelper.SetProperty(command!, "WorkingDirectory", tempDir.Path);
        ReflectionTestHelper.SetProperty(command!, "AppHostProjectPath", appHostProjectPath);
        ReflectionTestHelper.SetProperty(command!, "CommandKind", Enum.Parse(ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.AppServerCommandKind"), "GenerateTs"));
        ReflectionTestHelper.SetProperty(command!, "OutDirectory", Path.Combine(tempDir.Path, "out-ts"));
        ReflectionTestHelper.SetProperty(command!, "PrettierPath", Path.Combine(tempDir.Path, "prettier.cmd"));
        ReflectionTestHelper.SetProperty(command!, "Experimental", true);

        var launchSpec = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildLaunchSpec", command!);
        Assert.NotNull(launchSpec);

        var arguments = Assert.IsAssignableFrom<IReadOnlyList<string>>(ReflectionTestHelper.GetProperty(launchSpec!, "Arguments"));
        Assert.Equal(
            ["run", "--project", appHostProjectPath, "--", "app-server", "generate-ts", "--out", Path.Combine(tempDir.Path, "out-ts"), "--prettier", Path.Combine(tempDir.Path, "prettier.cmd"), "--experimental"],
            arguments);
    }

    [Fact]
    public void BuildMcpServerLaunchSpec_UsesDotNetRun_WhenBuiltKernelExecutableMissing()
    {
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.McpServerCommandRunner");
        var optionsType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.McpServerCommandOptions");
        using var tempDir = new TestTempDirectory();
        var appHostProjectPath = Path.Combine(tempDir.Path, "TianShu.AppHost.csproj");
        File.WriteAllText(appHostProjectPath, "<Project />");

        var command = Activator.CreateInstance(optionsType);
        Assert.NotNull(command);
        ReflectionTestHelper.SetProperty(command!, "WorkingDirectory", tempDir.Path);
        ReflectionTestHelper.SetProperty(command!, "AppHostProjectPath", appHostProjectPath);
        var defaultConfigPath = Assert.IsType<string>(ReflectionTestHelper.GetProperty(command!, "ConfigFilePath"));
        var overrides = Assert.IsAssignableFrom<System.Collections.IDictionary>(ReflectionTestHelper.GetProperty(command!, "ConfigOverrides"));
        overrides["sandbox_mode"] = "workspace-write";

        var launchSpec = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildLaunchSpec", command!);
        Assert.NotNull(launchSpec);
        Assert.Equal("dotnet", ReflectionTestHelper.GetProperty(launchSpec!, "ExecutablePath"));
        Assert.Equal(tempDir.Path, ReflectionTestHelper.GetProperty(launchSpec, "WorkingDirectory"));

        var arguments = Assert.IsAssignableFrom<IReadOnlyList<string>>(ReflectionTestHelper.GetProperty(launchSpec, "Arguments"));
        Assert.Equal(["run", "--project", appHostProjectPath, "--", "mcp-server", "--config-file", defaultConfigPath, "-c", "sandbox_mode=workspace-write"], arguments);
    }

    [Fact]
    public void BuildMcpServerLaunchSpec_UsesBuiltKernelExecutable_WhenAvailable()
    {
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.McpServerCommandRunner");
        var optionsType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.McpServerCommandOptions");
        using var tempDir = new TestTempDirectory();
        var appHostProjectPath = Path.Combine(tempDir.Path, "TianShu.AppHost.csproj");
        var executablePath = Path.Combine(tempDir.Path, "bin", "Debug", "net10.0", "TianShu.AppHost.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(executablePath)!);
        File.WriteAllText(appHostProjectPath, "<Project />");
        File.WriteAllText(executablePath, "stub");

        var command = Activator.CreateInstance(optionsType);
        Assert.NotNull(command);
        ReflectionTestHelper.SetProperty(command!, "WorkingDirectory", tempDir.Path);
        ReflectionTestHelper.SetProperty(command!, "AppHostProjectPath", appHostProjectPath);
        var defaultConfigPath = Assert.IsType<string>(ReflectionTestHelper.GetProperty(command!, "ConfigFilePath"));

        var launchSpec = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildLaunchSpec", command!);
        Assert.NotNull(launchSpec);
        Assert.Equal(executablePath, ReflectionTestHelper.GetProperty(launchSpec!, "ExecutablePath"));

        var arguments = Assert.IsAssignableFrom<IReadOnlyList<string>>(ReflectionTestHelper.GetProperty(launchSpec, "Arguments"));
        Assert.Equal(["mcp-server", "--config-file", defaultConfigPath], arguments);
    }

    [Fact]
    public void BuildMcpServerLaunchSpec_UsesPublishedKernelExecutable_WhenProjectMissing()
    {
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.McpServerCommandRunner");
        var optionsType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.McpServerCommandOptions");
        using var tempDir = new TestTempDirectory();
        var executablePath = Path.Combine(tempDir.Path, "TianShu.AppHost.exe");
        File.WriteAllText(executablePath, "stub");

        var command = Activator.CreateInstance(optionsType);
        Assert.NotNull(command);
        ReflectionTestHelper.SetProperty(command!, "WorkingDirectory", tempDir.Path);
        var defaultConfigPath = Assert.IsType<string>(ReflectionTestHelper.GetProperty(command!, "ConfigFilePath"));

        var launchSpec = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildLaunchSpec", command!);
        Assert.NotNull(launchSpec);
        Assert.Equal(executablePath, ReflectionTestHelper.GetProperty(launchSpec!, "ExecutablePath"));

        var arguments = Assert.IsAssignableFrom<IReadOnlyList<string>>(ReflectionTestHelper.GetProperty(launchSpec, "Arguments"));
        Assert.Equal(["mcp-server", "--config-file", defaultConfigPath], arguments);
    }

    [Fact]
    public void Parse_McpServerCommand_CollectsLaunchOptions()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "mcp-server", "--cwd", ".\\workspace", "--apphost-project", ".\\src\\Hosting\\TianShu.AppHost\\TianShu.AppHost.csproj", "--config-file", ".\\custom.toml", "-c", "sandbox_mode=read-only" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("McpServerCommandOptions", command!.GetType().Name);
        Assert.Equal(Path.GetFullPath(".\\workspace"), ReflectionTestHelper.GetProperty(command, "WorkingDirectory"));
        Assert.Equal(".\\src\\Hosting\\TianShu.AppHost\\TianShu.AppHost.csproj", ReflectionTestHelper.GetProperty(command, "AppHostProjectPath"));
        Assert.Equal(Path.GetFullPath(".\\custom.toml"), ReflectionTestHelper.GetProperty(command, "ConfigFilePath"));
        var overrides = Assert.IsAssignableFrom<System.Collections.IDictionary>(ReflectionTestHelper.GetProperty(command, "ConfigOverrides"));
        Assert.Equal("read-only", overrides["sandbox_mode"]);
    }

    [Fact]
    public void BuildAppServerLaunchSpec_ResolvesRelativeKernelProjectAgainstWorkingDirectory()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.AppServerCommandRunner");
        using var tempDir = new TestTempDirectory();
        var workspace = Path.Combine(tempDir.Path, "workspace");
        var appHostProjectPath = Path.Combine(workspace, "src", "Hosting", "TianShu.AppHost", "TianShu.AppHost.csproj");
        Directory.CreateDirectory(Path.GetDirectoryName(appHostProjectPath)!);
        File.WriteAllText(appHostProjectPath, "<Project />");

        var parseResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[]
            {
                "app-server",
                "--cwd", workspace,
                "--apphost-project", ".\\src\\Hosting\\TianShu.AppHost\\TianShu.AppHost.csproj",
            });
        var command = ReflectionTestHelper.GetProperty(parseResult!, "Command");
        Assert.NotNull(command);

        var launchSpec = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildLaunchSpec", command!);
        Assert.NotNull(launchSpec);

        var arguments = Assert.IsAssignableFrom<IReadOnlyList<string>>(ReflectionTestHelper.GetProperty(launchSpec!, "Arguments"));
        Assert.Equal(
            ["run", "--project", appHostProjectPath, "--", "app-server", "--listen", "stdio://", "--config-file", RuntimeConfigurationComposition.ResolveDefaultPath()],
            arguments);
    }

    [Fact]
    public void Parse_ChatCommand_SetsScript_And_JsonlProtocol()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "chat", "--script", ".\\chat.txt", "--protocol", "jsonl" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("ChatCommandOptions", command!.GetType().Name);
        Assert.Equal(Path.GetFullPath(".\\chat.txt"), ReflectionTestHelper.GetProperty(command, "ScriptPath"));
        Assert.Equal("Jsonl", ReflectionTestHelper.GetProperty(command, "OutputProtocol")?.ToString());
    }

    [Fact]
    public void BuildRuntimeSurfaceInvocation_SkillsList_MapsExtraRoots_And_Cwd()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        using var tempDir = new TestTempDirectory();
        var cwd = Path.Combine(tempDir.Path, "repo");
        var extra = Path.Combine(tempDir.Path, "skills-extra");
        Directory.CreateDirectory(cwd);
        Directory.CreateDirectory(extra);

        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "skills", "list", "--cwd", cwd, "--force-reload", "--extra-root", extra });
        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);

        var invocation = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildRuntimeSurfaceInvocation", command!);
        Assert.NotNull(invocation);
        Assert.Equal("skills/list", ReflectionTestHelper.GetProperty(invocation!, "Method"));

        var payload = ReflectionTestHelper.GetProperty(invocation!, "Parameters");
        var json = JsonSerializer.Serialize(payload);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(cwd, document.RootElement.GetProperty("cwds")[0].GetString());
        Assert.True(document.RootElement.GetProperty("forceReload").GetBoolean());
        Assert.Equal(cwd, document.RootElement.GetProperty("perCwdExtraUserRoots")[0].GetProperty("cwd").GetString());
        Assert.Equal(extra, document.RootElement.GetProperty("perCwdExtraUserRoots")[0].GetProperty("extraUserRoots")[0].GetString());
    }

    [Fact]
    public void BuildRuntimeSurfaceInvocation_ConfigRead_MapsIncludeLayers_And_Cwd()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        using var tempDir = new TestTempDirectory();

        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "config", "read", "--cwd", tempDir.Path, "--include-layers" });
        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);

        var invocation = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildRuntimeSurfaceInvocation", command!);
        Assert.NotNull(invocation);
        Assert.Equal("config/read", ReflectionTestHelper.GetProperty(invocation!, "Method"));

        var payload = ReflectionTestHelper.GetProperty(invocation!, "Parameters");
        var json = JsonSerializer.Serialize(payload);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(tempDir.Path, document.RootElement.GetProperty("cwd").GetString());
        Assert.True(document.RootElement.GetProperty("includeLayers").GetBoolean());
    }

    [Fact]
    public void BuildRuntimeSurfaceInvocation_PluginRead_MapsMarketplacePath_And_PluginName()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");

        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "plugin", "read", "--marketplace-path", "D:\\marketplace", "--plugin-name", "demo-plugin" });
        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);

        var invocation = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildRuntimeSurfaceInvocation", command!);
        Assert.NotNull(invocation);
        Assert.Equal("plugin/read", ReflectionTestHelper.GetProperty(invocation!, "Method"));

        var payload = ReflectionTestHelper.GetProperty(invocation!, "Parameters");
        var json = JsonSerializer.Serialize(payload);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(Path.GetFullPath("D:\\marketplace"), document.RootElement.GetProperty("marketplacePath").GetString());
        Assert.Equal("demo-plugin", document.RootElement.GetProperty("pluginName").GetString());
    }

    [Fact]
    public void BuildRuntimeSurfaceInvocation_PluginList_MapsCwd()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        using var tempDir = new TestTempDirectory();

        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "plugin", "list", "--cwd", tempDir.Path });
        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);

        var invocation = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildRuntimeSurfaceInvocation", command!);
        Assert.NotNull(invocation);
        Assert.Equal("plugin/list", ReflectionTestHelper.GetProperty(invocation!, "Method"));

        var payload = ReflectionTestHelper.GetProperty(invocation!, "Parameters");
        var json = JsonSerializer.Serialize(payload);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(tempDir.Path, document.RootElement.GetProperty("cwds")[0].GetString());
    }

    [Fact]
    public void Parse_SkillsEnableCommand_SetsPath_And_Enabled()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "skills", "enable", "--path", ".\\skills\\demo" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("RuntimeSurfaceCommandOptions", command!.GetType().Name);
        Assert.Equal("SkillsConfigWrite", ReflectionTestHelper.GetProperty(command, "CommandKind")?.ToString());
        Assert.Equal(Path.GetFullPath(".\\skills\\demo"), ReflectionTestHelper.GetProperty(command, "SkillPath"));
        Assert.Equal(true, ReflectionTestHelper.GetProperty(command, "Enabled"));
    }

    [Fact]
    public void BuildRuntimeSurfaceInvocation_SkillsConfigWrite_MapsPathEnabledAndCwd()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        using var tempDir = new TestTempDirectory();
        var skillPath = Path.Combine(tempDir.Path, "skills", "demo");
        Directory.CreateDirectory(skillPath);

        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "skills", "disable", "--cwd", tempDir.Path, "--path", skillPath });
        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);

        var invocation = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildRuntimeSurfaceInvocation", command!);
        Assert.NotNull(invocation);
        Assert.Equal("skills/config/write", ReflectionTestHelper.GetProperty(invocation!, "Method"));

        var payload = ReflectionTestHelper.GetProperty(invocation!, "Parameters");
        var json = JsonSerializer.Serialize(payload);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(skillPath, document.RootElement.GetProperty("path").GetString());
        Assert.False(document.RootElement.GetProperty("enabled").GetBoolean());
        Assert.Equal(tempDir.Path, document.RootElement.GetProperty("cwd").GetString());
    }

    [Fact]
    public void BuildRuntimeSurfaceInvocation_PluginInstall_MapsMarketplacePath_PluginName_AndCwd()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        using var tempDir = new TestTempDirectory();

        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "plugin", "install", "--cwd", tempDir.Path, "--marketplace-path", "D:\\marketplace", "--plugin-name", "demo-plugin" });
        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);

        var invocation = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildRuntimeSurfaceInvocation", command!);
        Assert.NotNull(invocation);
        Assert.Equal("plugin/install", ReflectionTestHelper.GetProperty(invocation!, "Method"));

        var payload = ReflectionTestHelper.GetProperty(invocation!, "Parameters");
        var json = JsonSerializer.Serialize(payload);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(Path.GetFullPath("D:\\marketplace"), document.RootElement.GetProperty("marketplacePath").GetString());
        Assert.Equal("demo-plugin", document.RootElement.GetProperty("pluginName").GetString());
        Assert.Equal(tempDir.Path, document.RootElement.GetProperty("cwd").GetString());
    }

    [Fact]
    public void BuildRuntimeSurfaceInvocation_ExperimentalFeatureList_MapsLimitAndCursor()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");

        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "experimental-feature", "list", "--limit", "7", "--cursor", "14" });
        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);

        var invocation = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildRuntimeSurfaceInvocation", command!);
        Assert.NotNull(invocation);
        Assert.Equal("experimentalfeature/list", ReflectionTestHelper.GetProperty(invocation!, "Method"));

        var payload = ReflectionTestHelper.GetProperty(invocation!, "Parameters");
        var json = JsonSerializer.Serialize(payload);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(7, document.RootElement.GetProperty("limit").GetInt32());
        Assert.Equal("14", document.RootElement.GetProperty("cursor").GetString());
    }

    [Fact]
    public void BuildRuntimeSurfaceInvocation_FeaturesEnable_MapsToConfigWritePayload()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        using var tempDir = new TestTempDirectory();

        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "features", "enable", "unified_exec", "--cwd", tempDir.Path });
        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);

        var invocation = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildRuntimeSurfaceInvocation", command!);
        Assert.NotNull(invocation);
        Assert.Equal("config/value/write", ReflectionTestHelper.GetProperty(invocation!, "Method"));

        var payload = ReflectionTestHelper.GetProperty(invocation!, "Parameters");
        var json = JsonSerializer.Serialize(payload);
        using var document = JsonDocument.Parse(json);
        Assert.Equal("features.unified_exec", document.RootElement.GetProperty("keyPath").GetString());
        Assert.True(document.RootElement.GetProperty("value").GetBoolean());
        Assert.Equal("replace", document.RootElement.GetProperty("mergeStrategy").GetString());
        Assert.Equal(tempDir.Path, document.RootElement.GetProperty("cwd").GetString());
    }

    [Fact]
    public void Parse_ConfigWriteCommand_SetsKey_ValueJson_And_FilePath()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        using var tempDir = new TestTempDirectory();
        var filePath = Path.Combine(tempDir.Path, "config.override.toml");

        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "config", "write", "--key", "shell_environment_policy.inherit", "--value-json", "false", "--merge-strategy", "upsert", "--file-path", filePath, "--expected-version", "v1" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("RuntimeSurfaceCommandOptions", command!.GetType().Name);
        Assert.Equal("ConfigValueWrite", ReflectionTestHelper.GetProperty(command, "CommandKind")?.ToString());
        Assert.Equal("shell_environment_policy.inherit", ReflectionTestHelper.GetProperty(command, "KeyPath"));
        Assert.Equal("false", ReflectionTestHelper.GetProperty(command, "ConfigValueJson"));
        Assert.Equal("upsert", ReflectionTestHelper.GetProperty(command, "MergeStrategy"));
        Assert.Equal(filePath, ReflectionTestHelper.GetProperty(command, "ConfigEditFilePath"));
        Assert.Equal("v1", ReflectionTestHelper.GetProperty(command, "ExpectedVersion"));
    }

    [Fact]
    public void BuildConfigValueWriteRequest_MapsKey_Value_FilePath_And_Version()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        using var tempDir = new TestTempDirectory();
        var valuePath = Path.Combine(tempDir.Path, "value.json");
        var filePath = Path.Combine(tempDir.Path, "config.override.toml");
        File.WriteAllText(valuePath, "false");

        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "config", "write", "--key", "shell_environment_policy.inherit", "--value-file", valuePath, "--merge-strategy", "upsert", "--file-path", filePath, "--expected-version", "v1" });
        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);

        var request = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildConfigValueWriteRequest", command!);
        Assert.NotNull(request);

        var typedValue = Assert.IsType<StructuredValue>(ReflectionTestHelper.GetProperty(request!, "Value"));
        Assert.Equal(StructuredValueKind.Boolean, typedValue.Kind);
        Assert.False(typedValue.BooleanValue);

        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var document = JsonDocument.Parse(json);
        Assert.Equal("shell_environment_policy.inherit", document.RootElement.GetProperty("keyPath").GetString());
        var value = document.RootElement.GetProperty("value");
        Assert.Equal(JsonValueKind.False, value.ValueKind);
        Assert.Equal("upsert", document.RootElement.GetProperty("mergeStrategy").GetString());
        Assert.Equal(Environment.CurrentDirectory, document.RootElement.GetProperty("workingDirectory").GetString());
        Assert.Equal(filePath, document.RootElement.GetProperty("filePath").GetString());
        Assert.Equal("v1", document.RootElement.GetProperty("expectedVersion").GetString());
    }

    [Fact]
    public void BuildRuntimeSurfaceInvocation_ConfigRequirementsRead_UsesExpectedMethod()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");

        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "config", "requirements" });
        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);

        var invocation = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildRuntimeSurfaceInvocation", command!);
        Assert.NotNull(invocation);
        Assert.Equal("configrequirements/read", ReflectionTestHelper.GetProperty(invocation!, "Method"));
        Assert.Null(ReflectionTestHelper.GetProperty(invocation!, "Parameters"));
    }

    [Fact]
    public void BuildConfigBatchWriteRequest_ReadsItemsFile()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        using var tempDir = new TestTempDirectory();
        var itemsPath = Path.Combine(tempDir.Path, "config-items.json");
        var filePath = Path.Combine(tempDir.Path, "config.override.toml");
        File.WriteAllText(itemsPath, "[{\"keyPath\":\"profiles.default.model\",\"value\":\"gpt-5\"},{\"keyPath\":\"shell_environment_policy.inherit\",\"value\":false,\"mergeStrategy\":\"replace\"}]");

        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "config", "batch-write", "--items-file", itemsPath, "--merge-strategy", "upsert", "--file-path", filePath, "--expected-version", "v2", "--reload-user-config" });
        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);

        var request = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildConfigBatchWriteRequest", command!);
        Assert.NotNull(request);

        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var document = JsonDocument.Parse(json);
        Assert.Equal(Environment.CurrentDirectory, document.RootElement.GetProperty("workingDirectory").GetString());
        Assert.Equal(filePath, document.RootElement.GetProperty("filePath").GetString());
        Assert.Equal("v2", document.RootElement.GetProperty("expectedVersion").GetString());
        Assert.True(document.RootElement.GetProperty("reloadUserConfig").GetBoolean());
        var items = document.RootElement.GetProperty("items");
        Assert.Equal(2, items.GetArrayLength());
        Assert.Equal("profiles.default.model", items[0].GetProperty("keyPath").GetString());
        Assert.Equal("upsert", items[0].GetProperty("mergeStrategy").GetString());
        Assert.Equal("shell_environment_policy.inherit", items[1].GetProperty("keyPath").GetString());
        Assert.Equal("replace", items[1].GetProperty("mergeStrategy").GetString());
    }

    [Fact]
    public void Parse_CommandExecCommand_SetsStreamingResize_And_ProcessId()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[]
            {
                "command", "exec", "--command", "git status", "--tty", "--process-id", "proc-1", "--rows", "40", "--cols", "120",
                "--stream-stdin", "--stream-stdout-stderr", "--timeout-ms", "5000", "--approval-policy", "on-request", "--json",
            });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("CommandExecCommandOptions", command!.GetType().Name);
        Assert.Equal("Exec", ReflectionTestHelper.GetProperty(command, "CommandKind")?.ToString());
        Assert.Equal("git status", ReflectionTestHelper.GetProperty(command, "CommandText"));
        Assert.Equal("proc-1", ReflectionTestHelper.GetProperty(command, "ProcessId"));
        Assert.Equal(true, ReflectionTestHelper.GetProperty(command, "Tty"));
        Assert.Equal(40, ReflectionTestHelper.GetProperty(command, "Rows"));
        Assert.Equal(120, ReflectionTestHelper.GetProperty(command, "Cols"));
        Assert.Equal(true, ReflectionTestHelper.GetProperty(command, "StreamStdin"));
        Assert.Equal(true, ReflectionTestHelper.GetProperty(command, "StreamStdoutStderr"));
        Assert.Equal(5000, ReflectionTestHelper.GetProperty(command, "TimeoutMs"));
        Assert.Equal("on-request", ReflectionTestHelper.GetProperty(command, "ApprovalPolicy"));
        Assert.Equal(true, ReflectionTestHelper.GetProperty(command, "OutputJson"));
    }

    [Fact]
    public void BuildCommandExecStartRequest_Exec_MapsArgvEnvSandbox_And_Size()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        using var tempDir = new TestTempDirectory();
        var argvPath = Path.Combine(tempDir.Path, "argv.json");
        var envPath = Path.Combine(tempDir.Path, "env.json");
        var sandboxPath = Path.Combine(tempDir.Path, "sandbox.json");
        File.WriteAllText(argvPath, "[\"cmd.exe\",\"/c\",\"echo hello\"]");
        File.WriteAllText(envPath, "{\"DEMO\":\"1\"}");
        File.WriteAllText(sandboxPath, "{\"mode\":\"workspace-write\"}");

        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "command", "exec", "--argv-file", argvPath, "--env-file", envPath, "--sandbox-file", sandboxPath, "--tty", "--process-id", "proc-2", "--rows", "24", "--cols", "80" });
        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);

        var request = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildCommandExecStartRequest", command!);
        Assert.NotNull(request);

        var typedRequest = Assert.IsType<ControlPlaneCommandExecutionStartCommand>(request);
        Assert.Null(typedRequest.CommandText);
        Assert.Collection(
            typedRequest.CommandArgs,
            item => Assert.Equal("cmd.exe", item),
            item => Assert.Equal("/c", item),
            item => Assert.Equal("echo hello", item));
        Assert.Equal("1", typedRequest.EnvironmentVariables["DEMO"]);
        Assert.NotNull(typedRequest.Sandbox);
        Assert.Equal(StructuredValueKind.Object, typedRequest.Sandbox!.Kind);
        Assert.Equal("workspace-write", typedRequest.Sandbox.Properties["mode"].StringValue);
        Assert.NotNull(typedRequest.Size);
        Assert.Equal((ushort)24, typedRequest.Size!.Rows);
        Assert.Equal((ushort)80, typedRequest.Size.Cols);
    }

    [Fact]
    public void BuildCommandExecWriteRequest_Write_EncodesTextAsBase64()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");

        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "command", "write", "--process-id", "proc-3", "--text", "你好" });
        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);

        var request = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildCommandExecWriteRequest", command!);
        Assert.NotNull(request);

        var typedRequest = Assert.IsType<ControlPlaneCommandExecutionWriteCommand>(request);
        Assert.Equal("proc-3", typedRequest.ProcessId);
        Assert.Equal(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("你好")), typedRequest.DeltaBase64);
    }

    [Fact]
    public void Parse_CodeModeExecCommand_SetsInputBudget_And_OutputJson()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "code-mode", "exec", "--thread-id", "thread-code-exec-001", "--input", "print('hi')", "--yield-time-ms", "250", "--max-output-tokens", "64", "--json" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("CodeModeCommandOptions", command!.GetType().Name);
        Assert.Equal("Exec", ReflectionTestHelper.GetProperty(command, "CommandKind")?.ToString());
        Assert.Equal("thread-code-exec-001", ReflectionTestHelper.GetProperty(command, "ThreadId"));
        Assert.Equal("print('hi')", ReflectionTestHelper.GetProperty(command, "Input"));
        Assert.Equal(250, ReflectionTestHelper.GetProperty(command, "YieldTimeMs"));
        Assert.Equal(64, ReflectionTestHelper.GetProperty(command, "MaxOutputTokens"));
        Assert.Equal(true, ReflectionTestHelper.GetProperty(command, "OutputJson"));
    }

    [Fact]
    public void Parse_CodeModeWaitCommand_SetsCellIdTerminate_And_MaxTokens()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "code-mode", "wait", "--thread-id", "thread-code-wait-001", "--cell-id", "cell-code-wait-001", "--yield-time-ms", "400", "--max-tokens", "32", "--terminate" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("CodeModeCommandOptions", command!.GetType().Name);
        Assert.Equal("Wait", ReflectionTestHelper.GetProperty(command, "CommandKind")?.ToString());
        Assert.Equal("thread-code-wait-001", ReflectionTestHelper.GetProperty(command, "ThreadId"));
        Assert.Equal("cell-code-wait-001", ReflectionTestHelper.GetProperty(command, "CellId"));
        Assert.Equal(400, ReflectionTestHelper.GetProperty(command, "YieldTimeMs"));
        Assert.Equal(32, ReflectionTestHelper.GetProperty(command, "MaxTokens"));
        Assert.Equal(true, ReflectionTestHelper.GetProperty(command, "Terminate"));
    }

    [Fact]
    public void Parse_McpListCommand_SetsKind()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "mcp", "list", "--json" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("McpCommandOptions", command!.GetType().Name);
        Assert.Equal("List", ReflectionTestHelper.GetProperty(command, "CommandKind")?.ToString());
        Assert.Equal(true, ReflectionTestHelper.GetProperty(command, "OutputJson"));
    }

    [Fact]
    public void Parse_McpGetCommand_SetsName()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "mcp", "get", "demo", "--json" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("McpCommandOptions", command!.GetType().Name);
        Assert.Equal("Get", ReflectionTestHelper.GetProperty(command, "CommandKind")?.ToString());
        Assert.Equal("demo", ReflectionTestHelper.GetProperty(command, "Name"));
        Assert.Equal(true, ReflectionTestHelper.GetProperty(command, "OutputJson"));
    }

    [Fact]
    public void Parse_SkillsRemoteListCommand_SetsRemoteFilters()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "skills", "remote-list", "--hazelnut-scope", "workspace", "--product-surface", "tianshu", "--enabled", "true", "--json" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("RuntimeSurfaceCommandOptions", command!.GetType().Name);
        Assert.Equal("SkillsRemoteList", ReflectionTestHelper.GetProperty(command, "CommandKind")?.ToString());
        Assert.Equal("workspace", ReflectionTestHelper.GetProperty(command, "HazelnutScope"));
        Assert.Equal("tianshu", ReflectionTestHelper.GetProperty(command, "ProductSurface"));
        Assert.Equal(true, ReflectionTestHelper.GetProperty(command, "RemoteEnabled"));
        Assert.Equal(true, ReflectionTestHelper.GetProperty(command, "OutputJson"));
    }

    [Fact]
    public void BuildRuntimeSurfaceInvocation_SkillsRemoteList_MapsRemoteFilters()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");

        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "skills", "remote-list", "--hazelnut-scope", "workspace", "--product-surface", "tianshu", "--enabled", "false" });
        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);

        var invocation = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildRuntimeSurfaceInvocation", command!);
        Assert.NotNull(invocation);
        Assert.Equal("skills/remote/list", ReflectionTestHelper.GetProperty(invocation!, "Method"));

        var payload = ReflectionTestHelper.GetProperty(invocation!, "Parameters");
        var json = JsonSerializer.Serialize(payload);
        using var document = JsonDocument.Parse(json);
        Assert.Equal("workspace", document.RootElement.GetProperty("hazelnutScope").GetString());
        Assert.Equal("tianshu", document.RootElement.GetProperty("productSurface").GetString());
        Assert.False(document.RootElement.GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public void Parse_SkillsRemoteExportCommand_SetsHazelnutId()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "skills", "remote-export", "--hazelnut-id", "hz_001" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("RuntimeSurfaceCommandOptions", command!.GetType().Name);
        Assert.Equal("SkillsRemoteExport", ReflectionTestHelper.GetProperty(command, "CommandKind")?.ToString());
        Assert.Equal("hz_001", ReflectionTestHelper.GetProperty(command, "HazelnutId"));
    }

    [Fact]
    public void BuildRuntimeSurfaceInvocation_SkillsRemoteExport_MapsHazelnutId()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");

        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "skills", "remote-export", "--hazelnut-id", "hz_001" });
        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);

        var invocation = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildRuntimeSurfaceInvocation", command!);
        Assert.NotNull(invocation);
        Assert.Equal("skills/remote/export", ReflectionTestHelper.GetProperty(invocation!, "Method"));

        var payload = ReflectionTestHelper.GetProperty(invocation!, "Parameters");
        var json = JsonSerializer.Serialize(payload);
        using var document = JsonDocument.Parse(json);
        Assert.Equal("hz_001", document.RootElement.GetProperty("hazelnutId").GetString());
    }

    [Fact]
    public void Parse_McpAddCommand_WithStdioCommand_SetsTransport()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "mcp", "add", "demo", "--env", "FOO=bar", "--", "node", "server.js", "--flag" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("McpCommandOptions", command!.GetType().Name);
        Assert.Equal("Add", ReflectionTestHelper.GetProperty(command, "CommandKind")?.ToString());
        Assert.Equal("demo", ReflectionTestHelper.GetProperty(command, "Name"));
        Assert.Null(ReflectionTestHelper.GetProperty(command, "Url"));
        Assert.Equal(["node", "server.js", "--flag"], Assert.IsAssignableFrom<IReadOnlyList<string>>(ReflectionTestHelper.GetProperty(command, "Command")));
        var env = Assert.IsAssignableFrom<System.Collections.IDictionary>(ReflectionTestHelper.GetProperty(command, "EnvironmentVariables"));
        Assert.Equal("bar", env["FOO"]);
    }

    [Fact]
    public void Parse_McpAddCommand_WithUrl_SetsTransport()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "mcp", "add", "demo-http", "--url", "https://example.com/mcp", "--bearer-token-env-var", "MCP_TOKEN" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("McpCommandOptions", command!.GetType().Name);
        Assert.Equal("Add", ReflectionTestHelper.GetProperty(command, "CommandKind")?.ToString());
        Assert.Equal("demo-http", ReflectionTestHelper.GetProperty(command, "Name"));
        Assert.Equal("https://example.com/mcp", ReflectionTestHelper.GetProperty(command, "Url"));
        Assert.Equal("MCP_TOKEN", ReflectionTestHelper.GetProperty(command, "BearerTokenEnvVar"));
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<string>>(ReflectionTestHelper.GetProperty(command, "Command")));
    }

    [Fact]
    public void Parse_AgentThreadRegister_SetsThreadIdNickname_AndRole()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "agent", "thread", "register", "--thread-id", "thread_001", "--nickname", "demo-agent", "--role", "reviewer", "--json" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("RuntimeSurfaceCommandOptions", command!.GetType().Name);
        Assert.Equal("AgentThreadRegister", ReflectionTestHelper.GetProperty(command, "CommandKind")?.ToString());
        Assert.Equal("thread_001", ReflectionTestHelper.GetProperty(command, "ThreadId"));
        Assert.Equal("demo-agent", ReflectionTestHelper.GetProperty(command, "AgentNickname"));
        Assert.Equal("reviewer", ReflectionTestHelper.GetProperty(command, "AgentRole"));
        Assert.Equal(true, ReflectionTestHelper.GetProperty(command, "OutputJson"));
    }

    [Fact]
    public void Parse_AgentJobCreate_SetsInstruction_JsonPayloads_AndPaths()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        using var tempDir = new TestTempDirectory();
        var inputCsvPath = Path.Combine(tempDir.Path, "input.csv");
        var outputCsvPath = Path.Combine(tempDir.Path, "output.csv");

        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[]
            {
                "agent", "job", "create",
                "--job-id", "job_001",
                "--name", "demo-job",
                "--instruction", "analyze items",
                "--input-headers-json", "{\"columns\":[\"title\"]}",
                "--input-csv-path", inputCsvPath,
                "--output-csv-path", outputCsvPath,
                "--auto-export", "false",
                "--output-schema-json", "{\"type\":\"object\"}",
                "--items-json", "[{\"itemId\":\"item-1\",\"sourceId\":\"src-1\"}]"
            });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("RuntimeSurfaceCommandOptions", command!.GetType().Name);
        Assert.Equal("AgentJobCreate", ReflectionTestHelper.GetProperty(command, "CommandKind")?.ToString());
        Assert.Equal("job_001", ReflectionTestHelper.GetProperty(command, "JobId"));
        Assert.Equal("demo-job", ReflectionTestHelper.GetProperty(command, "Name"));
        Assert.Equal("analyze items", ReflectionTestHelper.GetProperty(command, "Instruction"));
        Assert.Equal("{\"columns\":[\"title\"]}", ReflectionTestHelper.GetProperty(command, "InputHeadersJson"));
        Assert.Equal(inputCsvPath, ReflectionTestHelper.GetProperty(command, "InputCsvPath"));
        Assert.Equal(outputCsvPath, ReflectionTestHelper.GetProperty(command, "OutputCsvPath"));
        Assert.Equal(false, ReflectionTestHelper.GetProperty(command, "AutoExport"));
        Assert.Equal("{\"type\":\"object\"}", ReflectionTestHelper.GetProperty(command, "OutputSchemaJson"));
        Assert.Equal("[{\"itemId\":\"item-1\",\"sourceId\":\"src-1\"}]", ReflectionTestHelper.GetProperty(command, "ItemsJson"));
    }

    [Fact]
    public void Parse_AgentJobDispatch_SetsJobId_AndThreadIds()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "agent", "job", "dispatch", "--job-id", "job_001", "--thread-id", "thread-a", "--thread-id", "THREAD-A", "--thread-id", "thread-b" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("RuntimeSurfaceCommandOptions", command!.GetType().Name);
        Assert.Equal("AgentJobDispatch", ReflectionTestHelper.GetProperty(command, "CommandKind")?.ToString());
        Assert.Equal("job_001", ReflectionTestHelper.GetProperty(command, "JobId"));

        var threadIds = Assert.IsAssignableFrom<System.Collections.IEnumerable>(ReflectionTestHelper.GetProperty(command, "DispatchThreadIds"));
        Assert.Equal(new[] { "thread-a", "THREAD-A", "thread-b" }, threadIds.Cast<object>().Select(static item => item.ToString()).ToArray());
    }

    [Fact]
    public void Parse_AgentJobItemReport_SetsJobIdItemIdStatusResult_AndLastError()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "agent", "job", "report-item", "--job-id", "job_001", "--item-id", "item_001", "--status", "completed", "--result-json", "{\"score\":99}", "--last-error", "none" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("RuntimeSurfaceCommandOptions", command!.GetType().Name);
        Assert.Equal("AgentJobItemReport", ReflectionTestHelper.GetProperty(command, "CommandKind")?.ToString());
        Assert.Equal("job_001", ReflectionTestHelper.GetProperty(command, "JobId"));
        Assert.Equal("item_001", ReflectionTestHelper.GetProperty(command, "ItemId"));
        Assert.Equal("completed", ReflectionTestHelper.GetProperty(command, "Status"));
        Assert.Equal("{\"score\":99}", ReflectionTestHelper.GetProperty(command, "ResultJson"));
        Assert.Equal("none", ReflectionTestHelper.GetProperty(command, "LastError"));
    }

    [Fact]
    public void Parse_AgentJobRead_SetsJobId()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "agent", "job", "read", "--job-id", "job_001", "--json" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("RuntimeSurfaceCommandOptions", command!.GetType().Name);
        Assert.Equal("AgentJobRead", ReflectionTestHelper.GetProperty(command, "CommandKind")?.ToString());
        Assert.Equal("job_001", ReflectionTestHelper.GetProperty(command, "JobId"));
        Assert.Equal(true, ReflectionTestHelper.GetProperty(command, "OutputJson"));
    }

    [Fact]
    public void BuildRuntimeSurfaceInvocation_AgentFormalCommands_UseExpectedMethodsAndPayloads()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        using var tempDir = new TestTempDirectory();
        var inputCsvPath = Path.Combine(tempDir.Path, "input.csv");
        var outputCsvPath = Path.Combine(tempDir.Path, "output.csv");

        var registerResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "agent", "thread", "register", "--thread-id", "thread_001", "--agent-nickname", "demo-agent", "--agent-role", "reviewer" });
        var registerCommand = ReflectionTestHelper.GetProperty(registerResult!, "Command");
        Assert.NotNull(registerCommand);
        var registerInvocation = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildRuntimeSurfaceInvocation", registerCommand!);
        Assert.NotNull(registerInvocation);
        Assert.Equal("agent/thread/register", ReflectionTestHelper.GetProperty(registerInvocation!, "Method"));
        using (var registerPayload = JsonDocument.Parse(JsonSerializer.Serialize(ReflectionTestHelper.GetProperty(registerInvocation, "Parameters"))))
        {
            Assert.Equal("thread_001", registerPayload.RootElement.GetProperty("threadId").GetString());
            Assert.Equal("demo-agent", registerPayload.RootElement.GetProperty("agentNickname").GetString());
            Assert.Equal("reviewer", registerPayload.RootElement.GetProperty("agentRole").GetString());
        }

        var createResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[]
            {
                "agent", "job", "create",
                "--job-id", "job_001",
                "--name", "demo-job",
                "--instruction", "analyze items",
                "--input-headers-json", "{\"columns\":[\"title\"]}",
                "--input-csv-path", inputCsvPath,
                "--output-csv-path", outputCsvPath,
                "--auto-export", "false",
                "--output-schema-json", "{\"type\":\"object\"}",
                "--items-json", "[{\"itemId\":\"item-1\",\"sourceId\":\"src-1\"}]"
            });
        var createCommand = ReflectionTestHelper.GetProperty(createResult!, "Command");
        Assert.NotNull(createCommand);
        var createInvocation = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildRuntimeSurfaceInvocation", createCommand!);
        Assert.NotNull(createInvocation);
        Assert.Equal("agent/job/create", ReflectionTestHelper.GetProperty(createInvocation!, "Method"));
        using (var createPayload = JsonDocument.Parse(JsonSerializer.Serialize(ReflectionTestHelper.GetProperty(createInvocation, "Parameters"))))
        {
            Assert.Equal("job_001", createPayload.RootElement.GetProperty("jobId").GetString());
            Assert.Equal("demo-job", createPayload.RootElement.GetProperty("name").GetString());
            Assert.Equal("analyze items", createPayload.RootElement.GetProperty("instruction").GetString());
            Assert.Equal(inputCsvPath, createPayload.RootElement.GetProperty("inputCsvPath").GetString());
            Assert.Equal(outputCsvPath, createPayload.RootElement.GetProperty("outputCsvPath").GetString());
            Assert.False(createPayload.RootElement.GetProperty("autoExport").GetBoolean());
            Assert.Equal("title", createPayload.RootElement.GetProperty("inputHeaders").GetProperty("columns")[0].GetString());
            Assert.Equal("object", createPayload.RootElement.GetProperty("outputSchema").GetProperty("type").GetString());
            Assert.Equal("item-1", createPayload.RootElement.GetProperty("items")[0].GetProperty("itemId").GetString());
        }

        var dispatchResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "agent", "job", "dispatch", "--job-id", "job_001", "--thread-id", "thread-a", "--thread-id", "THREAD-A", "--thread-id", "thread-b" });
        var dispatchCommand = ReflectionTestHelper.GetProperty(dispatchResult!, "Command");
        Assert.NotNull(dispatchCommand);
        var dispatchInvocation = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildRuntimeSurfaceInvocation", dispatchCommand!);
        Assert.NotNull(dispatchInvocation);
        Assert.Equal("agent/job/dispatch", ReflectionTestHelper.GetProperty(dispatchInvocation!, "Method"));
        using (var dispatchPayload = JsonDocument.Parse(JsonSerializer.Serialize(ReflectionTestHelper.GetProperty(dispatchInvocation, "Parameters"))))
        {
            Assert.Equal("job_001", dispatchPayload.RootElement.GetProperty("jobId").GetString());
            Assert.Equal(new[] { "thread-a", "thread-b" }, dispatchPayload.RootElement.GetProperty("threadIds").EnumerateArray().Select(static item => item.GetString()).ToArray());
        }

        var reportResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "agent", "job", "report-item", "--job-id", "job_001", "--item-id", "item_001", "--status", "completed", "--result-json", "{\"score\":99}", "--last-error", "none" });
        var reportCommand = ReflectionTestHelper.GetProperty(reportResult!, "Command");
        Assert.NotNull(reportCommand);
        var reportInvocation = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildRuntimeSurfaceInvocation", reportCommand!);
        Assert.NotNull(reportInvocation);
        Assert.Equal("agent/job/item/report", ReflectionTestHelper.GetProperty(reportInvocation!, "Method"));
        using (var reportPayload = JsonDocument.Parse(JsonSerializer.Serialize(ReflectionTestHelper.GetProperty(reportInvocation, "Parameters"))))
        {
            Assert.Equal("job_001", reportPayload.RootElement.GetProperty("jobId").GetString());
            Assert.Equal("item_001", reportPayload.RootElement.GetProperty("itemId").GetString());
            Assert.Equal("completed", reportPayload.RootElement.GetProperty("status").GetString());
            Assert.Equal(99, reportPayload.RootElement.GetProperty("result").GetProperty("score").GetInt32());
            Assert.Equal("none", reportPayload.RootElement.GetProperty("lastError").GetString());
        }

        var readResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "agent", "job", "read", "--job-id", "job_001" });
        var readCommand = ReflectionTestHelper.GetProperty(readResult!, "Command");
        Assert.NotNull(readCommand);
        var readInvocation = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildRuntimeSurfaceInvocation", readCommand!);
        Assert.NotNull(readInvocation);
        Assert.Equal("agent/job/read", ReflectionTestHelper.GetProperty(readInvocation!, "Method"));
        using var readPayload = JsonDocument.Parse(JsonSerializer.Serialize(ReflectionTestHelper.GetProperty(readInvocation, "Parameters")));
        Assert.Equal("job_001", readPayload.RootElement.GetProperty("jobId").GetString());
    }
    [Fact]
    public void Parse_FuzzyFileSearchSearch_SetsQueryLimitAndRoots()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        using var tempDir = new TestTempDirectory();
        var rootA = Path.Combine(tempDir.Path, "src");
        var rootB = Path.Combine(tempDir.Path, "tests");
        Directory.CreateDirectory(rootA);
        Directory.CreateDirectory(rootB);

        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "fuzzy-file-search", "search", "--query", "Program.cs", "--limit", "20", "--root", rootA, "--root", rootB });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("FuzzyFileSearchCommandOptions", command!.GetType().Name);
        Assert.Equal("Search", ReflectionTestHelper.GetProperty(command, "CommandKind")?.ToString());
        Assert.Equal("Program.cs", ReflectionTestHelper.GetProperty(command, "Query"));
        Assert.Equal(20, ReflectionTestHelper.GetProperty(command, "Limit"));
        var roots = Assert.IsAssignableFrom<System.Collections.IEnumerable>(ReflectionTestHelper.GetProperty(command, "Roots"));
        var rootItems = roots.Cast<object>().Select(static item => item.ToString()).ToArray();
        Assert.Equal(new[] { rootA, rootB }, rootItems);
    }

    [Fact]
    public void BuildFuzzyFileSearchInvocation_Search_MapsRootsAndLimit()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        using var tempDir = new TestTempDirectory();
        var cwd = Path.Combine(tempDir.Path, "repo");
        var root = Path.Combine(tempDir.Path, "src");
        Directory.CreateDirectory(cwd);
        Directory.CreateDirectory(root);

        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "fuzzy-file-search", "search", "--query", "Kernel", "--limit", "7", "--root", root, "--cwd", cwd });
        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);

        var invocation = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildFuzzyFileSearchInvocation", command!);
        Assert.NotNull(invocation);
        Assert.Equal("fuzzyFileSearch", ReflectionTestHelper.GetProperty(invocation!, "Method"));

        var payload = ReflectionTestHelper.GetProperty(invocation!, "Parameters");
        var json = JsonSerializer.Serialize(payload);
        using var document = JsonDocument.Parse(json);
        Assert.Equal("Kernel", document.RootElement.GetProperty("query").GetString());
        Assert.Equal(7, document.RootElement.GetProperty("limit").GetInt32());
        Assert.Equal(cwd, document.RootElement.GetProperty("cwd").GetString());
        Assert.Equal(root, document.RootElement.GetProperty("roots")[0].GetString());
    }

        [Fact]
    public void Parse_FuzzyFileSearchUpdate_SetsSessionIdAndQuery()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        using var tempDir = new TestTempDirectory();
        var root = Path.Combine(tempDir.Path, "src");
        Directory.CreateDirectory(root);

        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "fuzzy-file-search", "update", "--session-id", "session-1", "--query", "AppHostServer", "--root", root });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("FuzzyFileSearchCommandOptions", command!.GetType().Name);
        Assert.Equal("Update", ReflectionTestHelper.GetProperty(command, "CommandKind")?.ToString());
        Assert.Equal("session-1", ReflectionTestHelper.GetProperty(command, "SessionId"));
        Assert.Equal("AppHostServer", ReflectionTestHelper.GetProperty(command, "Query"));
        var roots = Assert.IsAssignableFrom<System.Collections.IEnumerable>(ReflectionTestHelper.GetProperty(command, "Roots"));
        Assert.Equal(new[] { root }, roots.Cast<object>().Select(static item => item.ToString()).ToArray());
    }

    [Fact]
    public void BuildFuzzyFileSearchInvocation_Update_UsesExpectedMethod()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");

        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "fuzzy-file-search", "update", "--session-id", "session-2", "--query", "Program" });
        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);

        var invocation = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildFuzzyFileSearchInvocation", command!);
        Assert.NotNull(invocation);
        Assert.Equal("fuzzyFileSearch/sessionUpdate", ReflectionTestHelper.GetProperty(invocation!, "Method"));

        var payload = ReflectionTestHelper.GetProperty(invocation!, "Parameters");
        var json = JsonSerializer.Serialize(payload);
        using var document = JsonDocument.Parse(json);
        Assert.Equal("session-2", document.RootElement.GetProperty("sessionId").GetString());
        Assert.Equal("Program", document.RootElement.GetProperty("query").GetString());
    }

    [Fact]
    public void BuildFuzzyFileSearchSearchRequest_UsesTypedPayload()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        using var tempDir = new TestTempDirectory();
        var cwd = Path.Combine(tempDir.Path, "repo");
        var rootA = Path.Combine(tempDir.Path, "src");
        var rootB = Path.Combine(tempDir.Path, "tests");
        Directory.CreateDirectory(cwd);
        Directory.CreateDirectory(rootA);
        Directory.CreateDirectory(rootB);

        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "fuzzy-file-search", "search", "--query", "Kernel", "--limit", "7", "--root", rootA, "--root", rootB, "--cwd", cwd });
        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);

        var request = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildFuzzyFileSearchSearchRequest", command!);
        Assert.NotNull(request);
        Assert.Equal("Kernel", ReflectionTestHelper.GetProperty(request!, "Query"));
        Assert.Equal(7, ReflectionTestHelper.GetProperty(request, "Limit"));
        Assert.Equal(cwd, ReflectionTestHelper.GetProperty(request, "WorkingDirectory"));
        var roots = Assert.IsAssignableFrom<System.Collections.IEnumerable>(ReflectionTestHelper.GetProperty(request, "Roots"));
        Assert.Equal(new[] { rootA, rootB }, roots.Cast<object>().Select(static item => item.ToString()).ToArray());
    }

    [Fact]
    public void BuildFuzzyFileSearchSessionUpdateRequest_UsesTypedPayload()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");

        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "fuzzy-file-search", "update", "--session-id", "session-2", "--query", "Program" });
        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);

        var request = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildFuzzyFileSearchSessionUpdateRequest", command!);
        Assert.NotNull(request);
        Assert.Equal("session-2", ReflectionTestHelper.GetProperty(request!, "SessionId"));
        Assert.Equal("Program", ReflectionTestHelper.GetProperty(request, "Query"));
    }

    [Fact]
    public void IExecutionRuntime_ExposesTypedFuzzyFileSearchMethods()
    {
        var runtimeType = typeof(IExecutionRuntime);
        var allMethods = runtimeType
            .GetInterfaces()
            .Append(runtimeType)
            .SelectMany(static type => type.GetMethods())
            .ToArray();

        var searchMethod = allMethods.SingleOrDefault(static method => method.Name == "SearchFuzzyFilesAsync");
        var startMethod = allMethods.SingleOrDefault(static method => method.Name == "StartFuzzyFileSearchSessionAsync");
        var updateMethod = allMethods.SingleOrDefault(static method => method.Name == "UpdateFuzzyFileSearchSessionAsync");
        var stopMethod = allMethods.SingleOrDefault(static method => method.Name == "StopFuzzyFileSearchSessionAsync");

        Assert.NotNull(searchMethod);
        Assert.NotNull(startMethod);
        Assert.NotNull(updateMethod);
        Assert.NotNull(stopMethod);

        Assert.Equal("Task`1", searchMethod!.ReturnType.Name);
        Assert.Equal("Task`1", startMethod!.ReturnType.Name);
        Assert.Equal("Task`1", updateMethod!.ReturnType.Name);
        Assert.Equal("Task`1", stopMethod!.ReturnType.Name);
        Assert.Equal("ControlPlaneFuzzyFileSearchResult", searchMethod.ReturnType.GenericTypeArguments[0].Name);
        Assert.Equal("ControlPlaneFuzzyFileSearchCommandAcceptedResult", startMethod.ReturnType.GenericTypeArguments[0].Name);
        Assert.Equal("ControlPlaneFuzzyFileSearchCommandAcceptedResult", updateMethod.ReturnType.GenericTypeArguments[0].Name);
        Assert.Equal("ControlPlaneFuzzyFileSearchCommandAcceptedResult", stopMethod.ReturnType.GenericTypeArguments[0].Name);
    }

    [Fact]
    public void IExecutionRuntime_ExposesTypedUserShellMethod()
    {
        var runtimeType = typeof(IExecutionRuntime);
        var method = runtimeType.GetMethod("RunUserShellCommandAsync");

        Assert.NotNull(method);
        Assert.Equal("Task`1", method!.ReturnType.Name);
        Assert.Equal("ControlPlaneTurnSubmissionResult", method.ReturnType.GenericTypeArguments[0].Name);
    }

    [Fact]
    public void IExecutionRuntime_DoesNotExposeDiagnosticRpcFacade()
    {
        Assert.Null(typeof(IExecutionRuntime).GetMethod("InvokeDiagnosticRpcAsync"));
        Assert.NotNull(typeof(IExecutionRuntimeDiagnostics).GetMethod("InvokeDiagnosticRpcAsync"));
    }

    [Fact]
    public void IExecutionRuntime_ImplementsSplitControlPlaneClientInterfaces()
    {
        var runtimeType = typeof(IExecutionRuntime);
        var interfaces = runtimeType.GetInterfaces();

        Assert.Contains(typeof(ICollaborationControlPlaneClient), interfaces);
        Assert.Contains(typeof(ISessionControlPlaneClient), interfaces);
        Assert.Contains(typeof(IConversationControlPlaneClient), interfaces);
        Assert.Contains(typeof(IWorkflowControlPlaneClient), interfaces);
        Assert.Contains(typeof(IAgentControlPlaneClient), interfaces);
        Assert.Contains(typeof(IGovernanceControlPlaneClient), interfaces);
        Assert.Contains(typeof(ICatalogControlPlaneClient), interfaces);
        Assert.Contains(typeof(IIdentityControlPlaneClient), interfaces);
        Assert.Contains(typeof(IMemoryControlPlaneClient), interfaces);
        Assert.Contains(typeof(IArtifactControlPlaneClient), interfaces);
    }

    [Fact]
    public void TianShuExecutionRuntime_ImplementsSplitControlPlaneClientInterfaces()
    {
        Assert.True(typeof(ICollaborationControlPlaneClient).IsAssignableFrom(typeof(TianShuExecutionRuntime)));
        Assert.True(typeof(ISessionControlPlaneClient).IsAssignableFrom(typeof(TianShuExecutionRuntime)));
        Assert.True(typeof(IConversationControlPlaneClient).IsAssignableFrom(typeof(TianShuExecutionRuntime)));
        Assert.True(typeof(IWorkflowControlPlaneClient).IsAssignableFrom(typeof(TianShuExecutionRuntime)));
        Assert.True(typeof(IAgentControlPlaneClient).IsAssignableFrom(typeof(TianShuExecutionRuntime)));
        Assert.True(typeof(IGovernanceControlPlaneClient).IsAssignableFrom(typeof(TianShuExecutionRuntime)));
        Assert.True(typeof(ICatalogControlPlaneClient).IsAssignableFrom(typeof(TianShuExecutionRuntime)));
        Assert.True(typeof(IIdentityControlPlaneClient).IsAssignableFrom(typeof(TianShuExecutionRuntime)));
        Assert.True(typeof(IMemoryControlPlaneClient).IsAssignableFrom(typeof(TianShuExecutionRuntime)));
        Assert.True(typeof(IArtifactControlPlaneClient).IsAssignableFrom(typeof(TianShuExecutionRuntime)));
    }

    [Fact]
    public void Parse_ThreadLoadedListCommand_SetsKind_Limit_And_Cursor()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "thread", "loaded-list", "--limit", "9", "--cursor", "thread_002", "--json" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("ThreadCommandOptions", command!.GetType().Name);
        Assert.Equal("LoadedList", ReflectionTestHelper.GetProperty(command, "CommandKind")?.ToString());
        Assert.Equal(9, ReflectionTestHelper.GetProperty(command, "Limit"));
        Assert.Equal("thread_002", ReflectionTestHelper.GetProperty(command, "Cursor"));
        Assert.Equal(true, ReflectionTestHelper.GetProperty(command, "OutputJson"));
    }

    [Fact]
    public void BuildThreadRpcInvocation_LoadedList_MapsLimit_And_Cursor()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");

        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "thread", "loaded-list", "--limit", "4", "--cursor", "thread_010" });
        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);

        var invocation = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildThreadRpcInvocation", command!);
        Assert.NotNull(invocation);
        Assert.Equal("thread/loaded/list", ReflectionTestHelper.GetProperty(invocation!, "Method"));

        var payload = ReflectionTestHelper.GetProperty(invocation!, "Parameters");
        var json = JsonSerializer.Serialize(payload);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(4, document.RootElement.GetProperty("limit").GetInt32());
        Assert.Equal("thread_010", document.RootElement.GetProperty("cursor").GetString());
    }

    [Fact]
    public void Parse_ThreadStartCommand_SetsServiceName_And_HistoryFlags()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[]
            {
                "thread", "start",
                "--thread-service-name", "demo-service",
                "--thread-persist-extended-history", "true",
                "--thread-experimental-raw-events", "true",
            });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("ThreadCommandOptions", command!.GetType().Name);
        Assert.Equal("Start", ReflectionTestHelper.GetProperty(command, "CommandKind")?.ToString());
        Assert.Equal("demo-service", ReflectionTestHelper.GetProperty(command, "ThreadServiceName"));
        Assert.Equal(true, ReflectionTestHelper.GetProperty(command, "ThreadPersistExtendedHistory"));
        Assert.Equal(true, ReflectionTestHelper.GetProperty(command, "ThreadExperimentalRawEvents"));
    }

    [Fact]
    public void BuildThreadRequests_MapServiceName_And_PersistExtendedHistoryFlags()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");

        var startResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[]
            {
                "thread", "start",
                "--thread-service-name", "demo-service",
                "--thread-persist-extended-history", "true",
                "--thread-experimental-raw-events", "true",
            });
        var startCommand = ReflectionTestHelper.GetProperty(startResult!, "Command");
        Assert.NotNull(startCommand);
        var startRequest = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildControlPlaneThreadStartCommand", startCommand!);
        Assert.NotNull(startRequest);
        Assert.Equal("demo-service", ReflectionTestHelper.GetProperty(startRequest!, "ServiceName"));
        Assert.Equal(true, ReflectionTestHelper.GetProperty(startRequest, "PersistExtendedHistory"));
        Assert.Equal(true, ReflectionTestHelper.GetProperty(startRequest, "ExperimentalRawEvents"));

        var resumeResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "thread", "resume", "--thread-id", "thread_resume_001", "--thread-persist-extended-history", "true" });
        var resumeCommand = ReflectionTestHelper.GetProperty(resumeResult!, "Command");
        Assert.NotNull(resumeCommand);
        var resumeRequest = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildControlPlaneResumeThreadCommand", resumeCommand!);
        Assert.NotNull(resumeRequest);
        Assert.Equal(true, ReflectionTestHelper.GetProperty(resumeRequest!, "PersistExtendedHistory"));

        var forkResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "thread", "fork", "--thread-id", "thread_fork_001", "--thread-persist-extended-history", "true" });
        var forkCommand = ReflectionTestHelper.GetProperty(forkResult!, "Command");
        Assert.NotNull(forkCommand);
        var forkRequest = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildControlPlaneThreadForkCommand", forkCommand!);
        Assert.NotNull(forkRequest);
        Assert.Equal(true, ReflectionTestHelper.GetProperty(forkRequest!, "PersistExtendedHistory"));
    }

    [Fact]
    public void BuildExecThreadStartRequest_MapsEphemeralFlag()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.ExecCommandRunner");
        using var tempDirectory = new TestTempDirectory();
        var schemaPath = Path.Combine(tempDirectory.Path, "schema.json");
        var writableRootA = Path.Combine(tempDirectory.Path, "workspace-a");
        var writableRootB = Path.Combine(tempDirectory.Path, "workspace-b");
        File.WriteAllText(schemaPath, """{"type":"object"}""");
        Directory.CreateDirectory(writableRootA);
        Directory.CreateDirectory(writableRootB);

        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "exec", "--ephemeral", "--output-schema", schemaPath, "--add-dir", writableRootA, "--add-dir", writableRootB, "--model", "gpt-5", "hello" });
        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);

        var runtimeOptions = ReflectionTestHelper.InvokeMethod(command!, "ToRuntimeOptions");
        Assert.NotNull(runtimeOptions);
        var request = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildThreadStartCommand", runtimeOptions!, command!);
        Assert.NotNull(request);
        Assert.Equal(true, ReflectionTestHelper.GetProperty(request!, "Ephemeral"));
        var configuration = Assert.IsAssignableFrom<IReadOnlyDictionary<string, StructuredValue>>(
            ReflectionTestHelper.GetProperty(request, "Configuration"));
        var writableRoots = configuration["sandbox_workspace_write"]
            .Properties["writable_roots"]
            .Items
            .Select(static item => item.StringValue)
            .ToArray();
        Assert.Collection(
            writableRoots,
            item => Assert.Equal(writableRootA, item),
            item => Assert.Equal(writableRootB, item));

        var resumeRequest = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildThreadResumeCommand", "thread_exec_resume_001", runtimeOptions!, command!);
        Assert.NotNull(resumeRequest);
        var resumeConfiguration = Assert.IsAssignableFrom<IReadOnlyDictionary<string, StructuredValue>>(
            ReflectionTestHelper.GetProperty(resumeRequest!, "Configuration"));
        var resumeWritableRoots = resumeConfiguration["sandbox_workspace_write"]
            .Properties["writable_roots"]
            .Items
            .Select(static item => item.StringValue)
            .ToArray();
        Assert.Collection(
            resumeWritableRoots,
            item => Assert.Equal(writableRootA, item),
            item => Assert.Equal(writableRootB, item));
    }

    [Fact]
    public void Parse_ThreadStartCommand_ParsesTypedOverrides()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[]
            {
                "thread", "start",
                "--thread-service-tier", "null",
                "--thread-approval-policy", """{"granular":{"sandbox_approval":true,"rules":false,"skill_approval":true,"request_permissions":false,"mcp_elicitations":true}}""",
                "--thread-personality", "pragmatic",
                "--thread-dynamic-tools-json", """[{"name":"mcp__calendar__find_events","description":"搜索日历事件。","inputSchema":{"type":"object"}}]""",
            });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);

        var serviceTier = ReflectionTestHelper.GetProperty(command!, "ThreadServiceTier");
        Assert.NotNull(serviceTier);
        Assert.Equal("CliServiceTierOverride", serviceTier!.GetType().Name);
        Assert.True((bool)(ReflectionTestHelper.GetProperty(serviceTier, "IsSpecified") ?? false));
        Assert.True((bool)(ReflectionTestHelper.GetProperty(serviceTier, "IsCleared") ?? false));

        var approvalPolicy = Assert.IsType<string>(ReflectionTestHelper.GetProperty(command, "ThreadApprovalPolicy"));
        using (var approvalPolicyDocument = JsonDocument.Parse(approvalPolicy))
        {
            var granular = approvalPolicyDocument.RootElement.GetProperty("granular");
            Assert.True(granular.GetProperty("sandbox_approval").GetBoolean());
            Assert.False(granular.GetProperty("request_permissions").GetBoolean());
            Assert.True(granular.GetProperty("mcp_elicitations").GetBoolean());
        }

        var personality = Assert.IsType<string>(ReflectionTestHelper.GetProperty(command, "ThreadPersonality"));
        Assert.Equal("pragmatic", personality);

        var dynamicTools = Assert.IsAssignableFrom<System.Collections.IEnumerable>(ReflectionTestHelper.GetProperty(command, "ThreadDynamicTools"));
        var tool = Assert.Single(dynamicTools.Cast<object>());
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(tool, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal("mcp__calendar__find_events", document.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public void BuildThreadRequests_MapTypedOverrides()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");

        var resumeResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[]
            {
                "thread", "resume",
                "--thread-id", "thread_resume_typed_001",
                "--thread-service-tier", "null",
                "--thread-approval-policy", """{"granular":{"sandbox_approval":false,"rules":true,"skill_approval":false,"request_permissions":true,"mcp_elicitations":false}}""",
                "--thread-personality", "friendly",
                "--thread-history-json", """[{"type":"message","role":"user","content":[{"type":"input_text","text":"resume typed history"}]}]""",
            });
        var resumeCommand = ReflectionTestHelper.GetProperty(resumeResult!, "Command");
        Assert.NotNull(resumeCommand);

        var resumeRequest = Assert.IsType<ControlPlaneResumeThreadCommand>(
            ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildControlPlaneResumeThreadCommand", resumeCommand!));
        Assert.Equal("thread_resume_typed_001", resumeRequest.ThreadId.Value);
        Assert.Equal("null", resumeRequest.ServiceTier);
        Assert.NotNull(resumeRequest.ApprovalPolicy);
        using (var approvalDocument = JsonDocument.Parse(resumeRequest.ApprovalPolicy!))
        {
            Assert.True(approvalDocument.RootElement.GetProperty("granular").GetProperty("request_permissions").GetBoolean());
        }
        Assert.Equal("friendly", resumeRequest.Personality);
        var historyItem = Assert.Single(resumeRequest.History!);
        Assert.Equal("message", historyItem.Properties["type"].StringValue);
        Assert.Equal("user", historyItem.Properties["role"].StringValue);

        var forkResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[]
            {
                "thread", "fork",
                "--thread-id", "thread_fork_typed_001",
                "--thread-service-tier", "fast",
                "--thread-approval-policy", "never",
            });
        var forkCommand = ReflectionTestHelper.GetProperty(forkResult!, "Command");
        Assert.NotNull(forkCommand);

        var forkRequest = Assert.IsType<ControlPlaneForkThreadCommand>(
            ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildControlPlaneThreadForkCommand", forkCommand!));
        Assert.Equal("thread_fork_typed_001", forkRequest.ThreadId.Value);
        Assert.Equal("fast", forkRequest.ServiceTier);
        Assert.Equal("never", forkRequest.ApprovalPolicy);
    }

    [Fact]
    public void Parse_ThreadCommand_RejectsInvalidTypedOverrides()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");

        var invalidServiceTier = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "thread", "start", "--thread-service-tier", "priority" });
        var invalidServiceTierError = Assert.IsType<string>(ReflectionTestHelper.GetProperty(invalidServiceTier!, "ErrorMessage"));
        Assert.Contains("--thread-service-tier 只能是 fast、flex 或 null。", invalidServiceTierError, StringComparison.Ordinal);

        var invalidApprovalPolicy = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "thread", "start", "--thread-approval-policy", """{"granular":""" });
        var invalidApprovalPolicyError = Assert.IsType<string>(ReflectionTestHelper.GetProperty(invalidApprovalPolicy!, "ErrorMessage"));
        Assert.Contains("--thread-approval-policy JSON 解析失败", invalidApprovalPolicyError, StringComparison.Ordinal);

        var invalidPersonality = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "thread", "start", "--thread-personality", "balanced" });
        var invalidPersonalityError = Assert.IsType<string>(ReflectionTestHelper.GetProperty(invalidPersonality!, "ErrorMessage"));
        Assert.Contains("--thread-personality 只能是 none、friendly 或 pragmatic。", invalidPersonalityError, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_ThreadResumeCommand_RejectsExperimentalRawEvents()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "thread", "resume", "--thread-id", "thread_resume_001", "--thread-experimental-raw-events", "true" });
        Assert.NotNull(result);

        var errorMessage = Assert.IsType<string>(ReflectionTestHelper.GetProperty(result!, "ErrorMessage"));
        Assert.Contains("--thread-experimental-raw-events 只能与 start 一起使用", errorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_ThreadCompactCommand_SetsThreadId_And_KeepRecentTurns()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "thread", "compact", "--thread-id", "thread_123", "--keep-recent-turns", "6" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("ThreadCommandOptions", command!.GetType().Name);
        Assert.Equal("Compact", ReflectionTestHelper.GetProperty(command, "CommandKind")?.ToString());
        Assert.Equal("thread_123", ReflectionTestHelper.GetProperty(command, "ThreadId"));
        Assert.Equal(6, ReflectionTestHelper.GetProperty(command, "KeepRecentTurns"));
    }

    [Fact]
    public void BuildThreadRpcInvocation_Compact_UsesExpectedMethod()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");

        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "thread", "compact", "--thread-id", "thread_456", "--keep-recent", "3" });
        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);

        var invocation = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildThreadRpcInvocation", command!);
        Assert.NotNull(invocation);
        Assert.Equal("thread/compact/start", ReflectionTestHelper.GetProperty(invocation!, "Method"));

        var payload = ReflectionTestHelper.GetProperty(invocation!, "Parameters");
        var json = JsonSerializer.Serialize(payload);
        using var document = JsonDocument.Parse(json);
        Assert.Equal("thread_456", document.RootElement.GetProperty("threadId").GetString());
        Assert.Equal(3, document.RootElement.GetProperty("keepRecentTurns").GetInt32());
    }

    [Fact]
    public void BuildThreadRpcInvocation_CleanBackgroundTerminals_UsesExpectedMethod()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");

        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "thread", "clean-background-terminals", "--thread-id", "thread_clean_001" });
        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("CleanBackgroundTerminals", ReflectionTestHelper.GetProperty(command!, "CommandKind")?.ToString());

        var invocation = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildThreadRpcInvocation", command!);
        Assert.NotNull(invocation);
        Assert.Equal("thread/backgroundTerminals/clean", ReflectionTestHelper.GetProperty(invocation!, "Method"));

        var payload = ReflectionTestHelper.GetProperty(invocation!, "Parameters");
        var json = JsonSerializer.Serialize(payload);
        using var document = JsonDocument.Parse(json);
        Assert.Equal("thread_clean_001", document.RootElement.GetProperty("threadId").GetString());
    }

    [Fact]
    public void BuildThreadRpcInvocation_Unsubscribe_UsesExpectedMethod()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");

        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "thread", "unsubscribe", "--thread-id", "thread_unsub_001" });
        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("Unsubscribe", ReflectionTestHelper.GetProperty(command!, "CommandKind")?.ToString());

        var invocation = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildThreadRpcInvocation", command!);
        Assert.NotNull(invocation);
        Assert.Equal("thread/unsubscribe", ReflectionTestHelper.GetProperty(invocation!, "Method"));

        var payload = ReflectionTestHelper.GetProperty(invocation!, "Parameters");
        var json = JsonSerializer.Serialize(payload);
        using var document = JsonDocument.Parse(json);
        Assert.Equal("thread_unsub_001", document.RootElement.GetProperty("threadId").GetString());
    }

    [Fact]
    public void Parse_ThreadReadCommand_SetsThreadId_And_IncludeTurns()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "thread", "read", "--thread-id", "thread_read_001", "--include-turns", "--json" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("ThreadCommandOptions", command!.GetType().Name);
        Assert.Equal("Read", ReflectionTestHelper.GetProperty(command, "CommandKind")?.ToString());
        Assert.Equal("thread_read_001", ReflectionTestHelper.GetProperty(command, "ThreadId"));
        Assert.Equal(true, ReflectionTestHelper.GetProperty(command, "IncludeTurns"));
        Assert.Equal(true, ReflectionTestHelper.GetProperty(command, "OutputJson"));
    }

    [Fact]
    public void BuildThreadRpcInvocation_Read_UsesExpectedMethod()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");

        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "thread", "read", "--thread-id", "thread_read_002", "--include-turns" });
        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);

        var invocation = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildThreadRpcInvocation", command!);
        Assert.NotNull(invocation);
        Assert.Equal("thread/read", ReflectionTestHelper.GetProperty(invocation!, "Method"));

        var payload = ReflectionTestHelper.GetProperty(invocation!, "Parameters");
        var json = JsonSerializer.Serialize(payload);
        using var document = JsonDocument.Parse(json);
        Assert.Equal("thread_read_002", document.RootElement.GetProperty("threadId").GetString());
        Assert.True(document.RootElement.GetProperty("includeTurns").GetBoolean());
    }

    [Fact]
    public void Parse_ThreadUnarchiveCommand_SetsThreadId()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "thread", "unarchive", "--thread-id", "thread_unarchive_001" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("ThreadCommandOptions", command!.GetType().Name);
        Assert.Equal("Unarchive", ReflectionTestHelper.GetProperty(command, "CommandKind")?.ToString());
        Assert.Equal("thread_unarchive_001", ReflectionTestHelper.GetProperty(command, "ThreadId"));
    }

    [Fact]
    public void BuildThreadRpcInvocation_Unarchive_UsesExpectedMethod()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");

        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "thread", "unarchive", "--thread-id", "thread_unarchive_002" });
        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);

        var invocation = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildThreadRpcInvocation", command!);
        Assert.NotNull(invocation);
        Assert.Equal("thread/unarchive", ReflectionTestHelper.GetProperty(invocation!, "Method"));

        var payload = ReflectionTestHelper.GetProperty(invocation!, "Parameters");
        var json = JsonSerializer.Serialize(payload);
        using var document = JsonDocument.Parse(json);
        Assert.Equal("thread_unarchive_002", document.RootElement.GetProperty("threadId").GetString());
    }

    [Fact]
    public void Parse_ThreadMetadataCommand_SetsGitPatchFields()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[]
            {
                "thread", "metadata", "--thread-id", "thread_meta_001", "--git-sha", "abc123", "--clear-git-branch", "--git-origin-url", "https://example.com/repo.git",
            });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("ThreadCommandOptions", command!.GetType().Name);
        Assert.Equal("Metadata", ReflectionTestHelper.GetProperty(command, "CommandKind")?.ToString());
        Assert.Equal("thread_meta_001", ReflectionTestHelper.GetProperty(command, "ThreadId"));
        Assert.Equal("abc123", ReflectionTestHelper.GetProperty(command, "GitSha"));
        Assert.Equal(true, ReflectionTestHelper.GetProperty(command, "ClearGitBranch"));
        Assert.Equal("https://example.com/repo.git", ReflectionTestHelper.GetProperty(command, "GitOriginUrl"));
    }

    [Fact]
    public void BuildThreadRpcInvocation_Metadata_UsesExpectedMethod()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");

        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[]
            {
                "thread", "metadata", "--thread-id", "thread_meta_002", "--git-sha", "def456", "--clear-git-branch", "--git-origin-url", "https://example.com/repo.git",
            });
        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);

        var invocation = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildThreadRpcInvocation", command!);
        Assert.NotNull(invocation);
        Assert.Equal("thread/metadata/update", ReflectionTestHelper.GetProperty(invocation!, "Method"));

        var payload = ReflectionTestHelper.GetProperty(invocation!, "Parameters");
        var json = JsonSerializer.Serialize(payload);
        using var document = JsonDocument.Parse(json);
        Assert.Equal("thread_meta_002", document.RootElement.GetProperty("threadId").GetString());
        var gitInfo = document.RootElement.GetProperty("gitInfo");
        Assert.Equal("def456", gitInfo.GetProperty("sha").GetString());
        Assert.Equal(JsonValueKind.Null, gitInfo.GetProperty("branch").ValueKind);
        Assert.Equal("https://example.com/repo.git", gitInfo.GetProperty("originUrl").GetString());
    }

    [Fact]
    public void Parse_ThreadRollbackCommand_SetsThreadId_And_NumTurns()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "thread", "rollback", "--thread-id", "thread_rollback_001", "--num-turns", "2" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("ThreadCommandOptions", command!.GetType().Name);
        Assert.Equal("Rollback", ReflectionTestHelper.GetProperty(command, "CommandKind")?.ToString());
        Assert.Equal("thread_rollback_001", ReflectionTestHelper.GetProperty(command, "ThreadId"));
        Assert.Equal(2, ReflectionTestHelper.GetProperty(command, "NumTurns"));
    }

    [Fact]
    public void BuildThreadRpcInvocation_Rollback_UsesExpectedMethod()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");

        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "thread", "rollback", "--thread-id", "thread_rollback_002", "--num-turns", "3" });
        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);

        var invocation = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildThreadRpcInvocation", command!);
        Assert.NotNull(invocation);
        Assert.Equal("thread/rollback", ReflectionTestHelper.GetProperty(invocation!, "Method"));

        var payload = ReflectionTestHelper.GetProperty(invocation!, "Parameters");
        var json = JsonSerializer.Serialize(payload);
        using var document = JsonDocument.Parse(json);
        Assert.Equal("thread_rollback_002", document.RootElement.GetProperty("threadId").GetString());
        Assert.Equal(3, document.RootElement.GetProperty("numTurns").GetInt32());
    }


    [Fact]
    public void Parse_ConversationSummaryCommand_SetsThreadId()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "conversation-summary", "--thread-id", "thread_summary_001", "--json" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("RuntimeSurfaceCommandOptions", command!.GetType().Name);
        Assert.Equal("ConversationSummary", ReflectionTestHelper.GetProperty(command, "CommandKind")?.ToString());
        Assert.Equal("thread_summary_001", ReflectionTestHelper.GetProperty(command, "ThreadId"));
        Assert.Equal(true, ReflectionTestHelper.GetProperty(command, "OutputJson"));
    }

    [Fact]
    public void BuildRuntimeSurfaceInvocation_ConversationSummary_UsesRolloutPath()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        using var tempDir = new TestTempDirectory();
        var rolloutPath = Path.Combine(tempDir.Path, "rollout.jsonl");
        File.WriteAllText(rolloutPath, string.Empty);

        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "conversation-summary", "--rollout-path", rolloutPath });
        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);

        var invocation = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildRuntimeSurfaceInvocation", command!);
        Assert.NotNull(invocation);
        Assert.Equal("artifact/conversationsummary/read", ReflectionTestHelper.GetProperty(invocation!, "Method"));

        var payload = ReflectionTestHelper.GetProperty(invocation!, "Parameters");
        var json = JsonSerializer.Serialize(payload);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(rolloutPath, document.RootElement.GetProperty("rolloutPath").GetString());
    }

    [Fact]
    public void Parse_GitDiffCommand_SetsThreadId()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "git-diff", "--thread-id", "thread_diff_001" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("RuntimeSurfaceCommandOptions", command!.GetType().Name);
        Assert.Equal("GitDiffToRemote", ReflectionTestHelper.GetProperty(command, "CommandKind")?.ToString());
        Assert.Equal("thread_diff_001", ReflectionTestHelper.GetProperty(command, "ThreadId"));
    }

    [Fact]
    public void BuildRuntimeSurfaceInvocation_GitDiff_UsesExpectedMethod()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");

        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "git-diff", "--thread-id", "thread_diff_002" });
        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);

        var invocation = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildRuntimeSurfaceInvocation", command!);
        Assert.NotNull(invocation);
        Assert.Equal("artifact/gitdifftoremote/read", ReflectionTestHelper.GetProperty(invocation!, "Method"));

        var payload = ReflectionTestHelper.GetProperty(invocation!, "Parameters");
        var json = JsonSerializer.Serialize(payload);
        using var document = JsonDocument.Parse(json);
        Assert.Equal("thread_diff_002", document.RootElement.GetProperty("threadId").GetString());
    }

    [Fact]
    public void BuildRuntimeSurfaceInvocation_FormalReadQueries_UseExpectedMethodsAndPayloads()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");

        var sessionSnapshotResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "session", "snapshot" });
        var sessionSnapshotCommand = ReflectionTestHelper.GetProperty(sessionSnapshotResult!, "Command");
        Assert.NotNull(sessionSnapshotCommand);
        var sessionSnapshotInvocation = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildRuntimeSurfaceInvocation", sessionSnapshotCommand!);
        Assert.NotNull(sessionSnapshotInvocation);
        Assert.Equal("session/snapshot/read", ReflectionTestHelper.GetProperty(sessionSnapshotInvocation!, "Method"));
        using (var sessionSnapshotPayload = JsonDocument.Parse(JsonSerializer.Serialize(ReflectionTestHelper.GetProperty(sessionSnapshotInvocation, "Parameters"))))
        {
            Assert.Equal(JsonValueKind.Object, sessionSnapshotPayload.RootElement.ValueKind);
            Assert.Empty(sessionSnapshotPayload.RootElement.EnumerateObject());
        }

        var sessionResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "session", "list", "--collaboration-space-id", "space-expansion-001", "--include-closed" });
        var sessionCommand = ReflectionTestHelper.GetProperty(sessionResult!, "Command");
        Assert.NotNull(sessionCommand);
        var sessionInvocation = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildRuntimeSurfaceInvocation", sessionCommand!);
        Assert.NotNull(sessionInvocation);
        Assert.Equal("session/list", ReflectionTestHelper.GetProperty(sessionInvocation!, "Method"));
        using (var sessionPayload = JsonDocument.Parse(JsonSerializer.Serialize(ReflectionTestHelper.GetProperty(sessionInvocation, "Parameters"))))
        {
            Assert.Equal("space-expansion-001", sessionPayload.RootElement.GetProperty("collaborationSpaceId").GetString());
            Assert.True(sessionPayload.RootElement.GetProperty("includeClosed").GetBoolean());
        }

        var workflowResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "workflow", "board", "--workflow-id", "workflow-expansion-002" });
        var workflowCommand = ReflectionTestHelper.GetProperty(workflowResult!, "Command");
        Assert.NotNull(workflowCommand);
        var workflowInvocation = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildRuntimeSurfaceInvocation", workflowCommand!);
        Assert.NotNull(workflowInvocation);
        Assert.Equal("workflow/board/read", ReflectionTestHelper.GetProperty(workflowInvocation!, "Method"));
        using (var workflowPayload = JsonDocument.Parse(JsonSerializer.Serialize(ReflectionTestHelper.GetProperty(workflowInvocation, "Parameters"))))
        {
            Assert.Equal("workflow-expansion-002", workflowPayload.RootElement.GetProperty("workflowId").GetString());
        }

        var agentResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "agent", "roster", "--workflow-id", "workflow-agent-expansion-001" });
        var agentCommand = ReflectionTestHelper.GetProperty(agentResult!, "Command");
        Assert.NotNull(agentCommand);
        var agentInvocation = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildRuntimeSurfaceInvocation", agentCommand!);
        Assert.NotNull(agentInvocation);
        Assert.Equal("agent/roster/read", ReflectionTestHelper.GetProperty(agentInvocation!, "Method"));
        using var agentPayload = JsonDocument.Parse(JsonSerializer.Serialize(ReflectionTestHelper.GetProperty(agentInvocation, "Parameters")));
        Assert.Equal("workflow-agent-expansion-001", agentPayload.RootElement.GetProperty("workflowId").GetString());
    }

    [Fact]
    public void BuildRuntimeSurfaceInvocation_WorkflowFormalWriteCommands_UseExpectedMethodsAndPayloads()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");

        var createResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[]
            {
                "workflow",
                "create",
                "--workflow-id",
                "workflow-create-expansion-001",
                "--space-id",
                "space-expansion-001",
                "--display-name",
                "Workflow Expansion",
                "--thread-id",
                "thread-expansion-001",
                "--participant-id",
                "participant-expansion-001",
            });
        var createCommand = ReflectionTestHelper.GetProperty(createResult!, "Command");
        Assert.NotNull(createCommand);
        var createInvocation = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildRuntimeSurfaceInvocation", createCommand!);
        Assert.NotNull(createInvocation);
        Assert.Equal("workflow/create", ReflectionTestHelper.GetProperty(createInvocation!, "Method"));
        using (var createPayload = JsonDocument.Parse(JsonSerializer.Serialize(ReflectionTestHelper.GetProperty(createInvocation, "Parameters"))))
        {
            Assert.Equal("workflow-create-expansion-001", createPayload.RootElement.GetProperty("workflowId").GetString());
            Assert.Equal("space-expansion-001", createPayload.RootElement.GetProperty("spaceId").GetString());
            Assert.Equal("Workflow Expansion", createPayload.RootElement.GetProperty("displayName").GetString());
            Assert.Equal("thread-expansion-001", createPayload.RootElement.GetProperty("threadId").GetString());
            Assert.Equal("participant-expansion-001", createPayload.RootElement.GetProperty("participantId").GetString());
        }

        var publishResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[]
            {
                "workflow",
                "publish-plan",
                "--workflow-id",
                "workflow-create-expansion-001",
                "--title",
                "Expansion Plan",
                "--steps-json",
                """[{"title":"Wire workflow write host mirrors","description":"mirror cli and sidecar"}]""",
            });
        var publishCommand = ReflectionTestHelper.GetProperty(publishResult!, "Command");
        Assert.NotNull(publishCommand);
        var publishInvocation = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildRuntimeSurfaceInvocation", publishCommand!);
        Assert.NotNull(publishInvocation);
        Assert.Equal("workflow/plan/publish", ReflectionTestHelper.GetProperty(publishInvocation!, "Method"));
        using (var publishPayload = JsonDocument.Parse(JsonSerializer.Serialize(ReflectionTestHelper.GetProperty(publishInvocation, "Parameters"))))
        {
            Assert.Equal("workflow-create-expansion-001", publishPayload.RootElement.GetProperty("workflowId").GetString());
            Assert.Equal("Expansion Plan", publishPayload.RootElement.GetProperty("title").GetString());
            Assert.Equal("Wire workflow write host mirrors", publishPayload.RootElement.GetProperty("steps")[0].GetProperty("title").GetString());
        }

        var createTaskResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[]
            {
                "workflow",
                "create-task",
                "--task-id",
                "task-expansion-001",
                "--workflow-id",
                "workflow-create-expansion-001",
                "--title",
                "Implement workflow command tree",
                "--state",
                "in-progress",
                "--participant-id",
                "participant-expansion-001",
            });
        var createTaskCommand = ReflectionTestHelper.GetProperty(createTaskResult!, "Command");
        Assert.NotNull(createTaskCommand);
        var createTaskInvocation = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildRuntimeSurfaceInvocation", createTaskCommand!);
        Assert.NotNull(createTaskInvocation);
        Assert.Equal("workflow/task/create", ReflectionTestHelper.GetProperty(createTaskInvocation!, "Method"));
        using (var createTaskPayload = JsonDocument.Parse(JsonSerializer.Serialize(ReflectionTestHelper.GetProperty(createTaskInvocation, "Parameters"))))
        {
            Assert.Equal("task-expansion-001", createTaskPayload.RootElement.GetProperty("taskId").GetString());
            Assert.Equal("workflow-create-expansion-001", createTaskPayload.RootElement.GetProperty("workflowId").GetString());
            Assert.Equal("Implement workflow command tree", createTaskPayload.RootElement.GetProperty("title").GetString());
            Assert.Equal("in-progress", createTaskPayload.RootElement.GetProperty("state").GetString());
        }

        var updateTaskResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[]
            {
                "workflow",
                "update-task-state",
                "--task-id",
                "task-expansion-001",
                "--state",
                "done",
                "--participant-id",
                "participant-expansion-001",
            });
        var updateTaskCommand = ReflectionTestHelper.GetProperty(updateTaskResult!, "Command");
        Assert.NotNull(updateTaskCommand);
        var updateTaskInvocation = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildRuntimeSurfaceInvocation", updateTaskCommand!);
        Assert.NotNull(updateTaskInvocation);
        Assert.Equal("workflow/task/updatestate", ReflectionTestHelper.GetProperty(updateTaskInvocation!, "Method"));
        using var updateTaskPayload = JsonDocument.Parse(JsonSerializer.Serialize(ReflectionTestHelper.GetProperty(updateTaskInvocation, "Parameters")));
        Assert.Equal("task-expansion-001", updateTaskPayload.RootElement.GetProperty("taskId").GetString());
        Assert.Equal("done", updateTaskPayload.RootElement.GetProperty("state").GetString());
    }

    [Fact]
    public void BuildRuntimeSurfaceInvocation_CollaborationAndParticipantQueries_UseExpectedMethodsAndPayloads()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");

        var collaborationResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "collaboration", "list", "--include-archived" });
        var collaborationCommand = ReflectionTestHelper.GetProperty(collaborationResult!, "Command");
        Assert.NotNull(collaborationCommand);
        var collaborationInvocation = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildRuntimeSurfaceInvocation", collaborationCommand!);
        Assert.NotNull(collaborationInvocation);
        Assert.Equal("collaboration/list", ReflectionTestHelper.GetProperty(collaborationInvocation!, "Method"));
        using (var collaborationPayload = JsonDocument.Parse(JsonSerializer.Serialize(ReflectionTestHelper.GetProperty(collaborationInvocation, "Parameters"))))
        {
            Assert.True(collaborationPayload.RootElement.GetProperty("includeArchived").GetBoolean());
        }

        var participantResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "participant", "view", "--participant-id", "participant-expansion-002" });
        var participantCommand = ReflectionTestHelper.GetProperty(participantResult!, "Command");
        Assert.NotNull(participantCommand);
        var participantInvocation = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildRuntimeSurfaceInvocation", participantCommand!);
        Assert.NotNull(participantInvocation);
        Assert.Equal("participant/view/read", ReflectionTestHelper.GetProperty(participantInvocation!, "Method"));
        using (var participantPayload = JsonDocument.Parse(JsonSerializer.Serialize(ReflectionTestHelper.GetProperty(participantInvocation, "Parameters"))))
        {
            Assert.Equal("participant-expansion-002", participantPayload.RootElement.GetProperty("participantId").GetString());
        }

        var participantListResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "participant", "list", "--space-id", "space-expansion-003" });
        var participantListCommand = ReflectionTestHelper.GetProperty(participantListResult!, "Command");
        Assert.NotNull(participantListCommand);
        var participantListInvocation = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildRuntimeSurfaceInvocation", participantListCommand!);
        Assert.NotNull(participantListInvocation);
        Assert.Equal("participant/list", ReflectionTestHelper.GetProperty(participantListInvocation!, "Method"));
        using var participantListPayload = JsonDocument.Parse(JsonSerializer.Serialize(ReflectionTestHelper.GetProperty(participantListInvocation, "Parameters")));
        Assert.Equal("space-expansion-003", participantListPayload.RootElement.GetProperty("spaceId").GetString());
    }

    [Fact]
    public void BuildRuntimeSurfaceInvocation_CollaborationFormalCommands_UseExpectedMethodsAndPayloads()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");

        var createResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[]
            {
                "collaboration",
                "create",
                "--space-id",
                "space-create-001",
                "--key",
                "team-alpha",
                "--display-name",
                "Team Alpha",
                "--purpose",
                "Cross repo collaboration",
                "--default-workspace",
                "D:/Repos/TianShu",
                "--default-execution-profile",
                "review",
                "--policy-key",
                "policy-alpha",
            });
        var createCommand = ReflectionTestHelper.GetProperty(createResult!, "Command");
        Assert.NotNull(createCommand);
        var createInvocation = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildRuntimeSurfaceInvocation", createCommand!);
        Assert.NotNull(createInvocation);
        Assert.Equal("collaboration/create", ReflectionTestHelper.GetProperty(createInvocation!, "Method"));
        using (var createPayload = JsonDocument.Parse(JsonSerializer.Serialize(ReflectionTestHelper.GetProperty(createInvocation, "Parameters"))))
        {
            Assert.Equal("space-create-001", createPayload.RootElement.GetProperty("spaceId").GetString());
            Assert.Equal("team-alpha", createPayload.RootElement.GetProperty("key").GetString());
            Assert.Equal("Team Alpha", createPayload.RootElement.GetProperty("displayName").GetString());
            Assert.Equal("Cross repo collaboration", createPayload.RootElement.GetProperty("purpose").GetString());
            Assert.Equal(Path.GetFullPath("D:/Repos/TianShu"), createPayload.RootElement.GetProperty("defaultWorkspace").GetString());
            Assert.Equal("review", createPayload.RootElement.GetProperty("defaultExecutionProfile").GetString());
            Assert.Equal("policy-alpha", createPayload.RootElement.GetProperty("policyKey").GetString());
        }

        var configureResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "collaboration", "configure", "--space-id", "space-create-001", "--display-name", "Team Alpha v2", "--purpose", "Updated purpose" });
        var configureCommand = ReflectionTestHelper.GetProperty(configureResult!, "Command");
        Assert.NotNull(configureCommand);
        var configureInvocation = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildRuntimeSurfaceInvocation", configureCommand!);
        Assert.NotNull(configureInvocation);
        Assert.Equal("collaboration/configure", ReflectionTestHelper.GetProperty(configureInvocation!, "Method"));

        var archiveResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "collaboration", "archive", "--space-id", "space-create-001" });
        var archiveCommand = ReflectionTestHelper.GetProperty(archiveResult!, "Command");
        Assert.NotNull(archiveCommand);
        var archiveInvocation = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildRuntimeSurfaceInvocation", archiveCommand!);
        Assert.NotNull(archiveInvocation);
        Assert.Equal("collaboration/archive", ReflectionTestHelper.GetProperty(archiveInvocation!, "Method"));

        var bindSessionResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "participant", "bind-session", "--participant-id", "participant-001", "--session-id", "session-001" });
        var bindSessionCommand = ReflectionTestHelper.GetProperty(bindSessionResult!, "Command");
        Assert.NotNull(bindSessionCommand);
        var bindSessionInvocation = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildRuntimeSurfaceInvocation", bindSessionCommand!);
        Assert.NotNull(bindSessionInvocation);
        Assert.Equal("participant/bindsession", ReflectionTestHelper.GetProperty(bindSessionInvocation!, "Method"));
        using (var bindSessionPayload = JsonDocument.Parse(JsonSerializer.Serialize(ReflectionTestHelper.GetProperty(bindSessionInvocation, "Parameters"))))
        {
            Assert.Equal("session-001", bindSessionPayload.RootElement.GetProperty("sessionId").GetString());
            Assert.Equal("participant-001", bindSessionPayload.RootElement.GetProperty("participantId").GetString());
        }

        var bindWorkflowResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "participant", "bind-workflow", "--participant-id", "participant-002", "--workflow-id", "workflow-002" });
        var bindWorkflowCommand = ReflectionTestHelper.GetProperty(bindWorkflowResult!, "Command");
        Assert.NotNull(bindWorkflowCommand);
        var bindWorkflowInvocation = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildRuntimeSurfaceInvocation", bindWorkflowCommand!);
        Assert.NotNull(bindWorkflowInvocation);
        Assert.Equal("participant/bindworkflow", ReflectionTestHelper.GetProperty(bindWorkflowInvocation!, "Method"));

        var updateRoleResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "participant", "update-role", "--participant-id", "participant-003", "--role", "owner" });
        var updateRoleCommand = ReflectionTestHelper.GetProperty(updateRoleResult!, "Command");
        Assert.NotNull(updateRoleCommand);
        var updateRoleInvocation = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildRuntimeSurfaceInvocation", updateRoleCommand!);
        Assert.NotNull(updateRoleInvocation);
        Assert.Equal("participant/updaterole", ReflectionTestHelper.GetProperty(updateRoleInvocation!, "Method"));
        using var updateRolePayload = JsonDocument.Parse(JsonSerializer.Serialize(ReflectionTestHelper.GetProperty(updateRoleInvocation, "Parameters")));
        Assert.Equal("owner", updateRolePayload.RootElement.GetProperty("role").GetString());
    }

    [Fact]
    public void BuildRuntimeSurfaceInvocation_ConversationGovernanceAndArtifactQueries_UseExpectedMethodsAndPayloads()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");

        var conversationResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "conversation", "read", "--thread-id", "thread-expansion-002" });
        var conversationCommand = ReflectionTestHelper.GetProperty(conversationResult!, "Command");
        Assert.NotNull(conversationCommand);
        var conversationInvocation = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildRuntimeSurfaceInvocation", conversationCommand!);
        Assert.NotNull(conversationInvocation);
        Assert.Equal("conversation/thread/read", ReflectionTestHelper.GetProperty(conversationInvocation!, "Method"));
        using (var conversationPayload = JsonDocument.Parse(JsonSerializer.Serialize(ReflectionTestHelper.GetProperty(conversationInvocation, "Parameters"))))
        {
            Assert.Equal("thread-expansion-002", conversationPayload.RootElement.GetProperty("threadId").GetString());
        }

        var approvalResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "governance", "approvals", "--requested-from-participant-id", "participant-expansion-003" });
        var approvalCommand = ReflectionTestHelper.GetProperty(approvalResult!, "Command");
        Assert.NotNull(approvalCommand);
        var approvalInvocation = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildRuntimeSurfaceInvocation", approvalCommand!);
        Assert.NotNull(approvalInvocation);
        Assert.Equal("governance/approvalqueue/read", ReflectionTestHelper.GetProperty(approvalInvocation!, "Method"));
        using (var approvalPayload = JsonDocument.Parse(JsonSerializer.Serialize(ReflectionTestHelper.GetProperty(approvalInvocation, "Parameters"))))
        {
            Assert.Equal("participant-expansion-003", approvalPayload.RootElement.GetProperty("participantId").GetString());
        }

        var governanceResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "governance", "user-inputs", "--requested-from-participant-id", "participant-expansion-004" });
        var governanceCommand = ReflectionTestHelper.GetProperty(governanceResult!, "Command");
        Assert.NotNull(governanceCommand);
        var governanceInvocation = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildRuntimeSurfaceInvocation", governanceCommand!);
        Assert.NotNull(governanceInvocation);
        Assert.Equal("governance/userinputs/list", ReflectionTestHelper.GetProperty(governanceInvocation!, "Method"));
        using (var governancePayload = JsonDocument.Parse(JsonSerializer.Serialize(ReflectionTestHelper.GetProperty(governanceInvocation, "Parameters"))))
        {
            Assert.Equal("participant-expansion-004", governancePayload.RootElement.GetProperty("participantId").GetString());
        }

        var artifactResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "artifact", "list", "--space-id", "space-expansion-004", "--produced-by-participant-id", "participant-expansion-005" });
        var artifactCommand = ReflectionTestHelper.GetProperty(artifactResult!, "Command");
        Assert.NotNull(artifactCommand);
        var artifactInvocation = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildRuntimeSurfaceInvocation", artifactCommand!);
        Assert.NotNull(artifactInvocation);
        Assert.Equal("artifact/collection/read", ReflectionTestHelper.GetProperty(artifactInvocation!, "Method"));
        using var artifactPayload = JsonDocument.Parse(JsonSerializer.Serialize(ReflectionTestHelper.GetProperty(artifactInvocation, "Parameters")));
        Assert.Equal("space-expansion-004", artifactPayload.RootElement.GetProperty("spaceId").GetString());
        Assert.Equal("participant-expansion-005", artifactPayload.RootElement.GetProperty("participantId").GetString());
    }

    [Fact]
    public void BuildRuntimeSurfaceInvocation_DiagnosticsQueries_UseExpectedMethodsAndPayloads()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");

        var traceResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "diagnostics", "trace", "--trace-id", "trace-expansion-001" });
        var traceCommand = ReflectionTestHelper.GetProperty(traceResult!, "Command");
        Assert.NotNull(traceCommand);
        var traceInvocation = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildRuntimeSurfaceInvocation", traceCommand!);
        Assert.NotNull(traceInvocation);
        Assert.Equal("diagnostics/trace/read", ReflectionTestHelper.GetProperty(traceInvocation!, "Method"));
        using (var tracePayload = JsonDocument.Parse(JsonSerializer.Serialize(ReflectionTestHelper.GetProperty(traceInvocation, "Parameters"))))
        {
            Assert.Equal("trace-expansion-001", tracePayload.RootElement.GetProperty("traceId").GetString());
        }

        var attemptsResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "diagnostics", "attempts", "--execution-id", "execution-expansion-001" });
        var attemptsCommand = ReflectionTestHelper.GetProperty(attemptsResult!, "Command");
        Assert.NotNull(attemptsCommand);
        var attemptsInvocation = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildRuntimeSurfaceInvocation", attemptsCommand!);
        Assert.NotNull(attemptsInvocation);
        Assert.Equal("diagnostics/attempts/list", ReflectionTestHelper.GetProperty(attemptsInvocation!, "Method"));
        using var attemptsPayload = JsonDocument.Parse(JsonSerializer.Serialize(ReflectionTestHelper.GetProperty(attemptsInvocation, "Parameters")));
        Assert.Equal("execution-expansion-001", attemptsPayload.RootElement.GetProperty("executionId").GetString());
    }

    [Fact]
    public void BuildRuntimeSurfaceInvocation_IdentityAndMemoryQueries_UseExpectedMethodsAndPayloads()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");

        var accountResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "identity", "account", "--account-id", "account-expansion-001" });
        var accountCommand = ReflectionTestHelper.GetProperty(accountResult!, "Command");
        Assert.NotNull(accountCommand);
        var accountInvocation = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildRuntimeSurfaceInvocation", accountCommand!);
        Assert.NotNull(accountInvocation);
        Assert.Equal("identity/account/read", ReflectionTestHelper.GetProperty(accountInvocation!, "Method"));
        using (var accountPayload = JsonDocument.Parse(JsonSerializer.Serialize(ReflectionTestHelper.GetProperty(accountInvocation, "Parameters"))))
        {
            Assert.Equal("account-expansion-001", accountPayload.RootElement.GetProperty("accountId").GetString());
        }

        var devicesResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "identity", "devices", "--account-id", "account-expansion-002" });
        var devicesCommand = ReflectionTestHelper.GetProperty(devicesResult!, "Command");
        Assert.NotNull(devicesCommand);
        var devicesInvocation = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildRuntimeSurfaceInvocation", devicesCommand!);
        Assert.NotNull(devicesInvocation);
        Assert.Equal("identity/devices/list", ReflectionTestHelper.GetProperty(devicesInvocation!, "Method"));
        using (var devicesPayload = JsonDocument.Parse(JsonSerializer.Serialize(ReflectionTestHelper.GetProperty(devicesInvocation, "Parameters"))))
        {
            Assert.Equal("account-expansion-002", devicesPayload.RootElement.GetProperty("accountId").GetString());
        }

        var spacesResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "memory", "spaces", "--scope-kind", "workspace" });
        var spacesCommand = ReflectionTestHelper.GetProperty(spacesResult!, "Command");
        Assert.NotNull(spacesCommand);
        var spacesInvocation = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildRuntimeSurfaceInvocation", spacesCommand!);
        Assert.NotNull(spacesInvocation);
        Assert.Equal("memory/spaces/list", ReflectionTestHelper.GetProperty(spacesInvocation!, "Method"));
        using (var spacesPayload = JsonDocument.Parse(JsonSerializer.Serialize(ReflectionTestHelper.GetProperty(spacesInvocation, "Parameters"))))
        {
            Assert.Equal("workspace", spacesPayload.RootElement.GetProperty("scopeKind").GetString());
        }

        var overlayResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "memory", "overlay", "--memory-space-id", "memory-space-expansion-001", "--space-id", "space-expansion-005" });
        var overlayCommand = ReflectionTestHelper.GetProperty(overlayResult!, "Command");
        Assert.NotNull(overlayCommand);
        var overlayInvocation = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildRuntimeSurfaceInvocation", overlayCommand!);
        Assert.NotNull(overlayInvocation);
        Assert.Equal("memory/overlay/read", ReflectionTestHelper.GetProperty(overlayInvocation!, "Method"));
        using var overlayPayload = JsonDocument.Parse(JsonSerializer.Serialize(ReflectionTestHelper.GetProperty(overlayInvocation, "Parameters")));
        Assert.Equal("memory-space-expansion-001", overlayPayload.RootElement.GetProperty("memorySpaceId").GetString());
        Assert.Equal("space-expansion-005", overlayPayload.RootElement.GetProperty("spaceId").GetString());

        var providersResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "memory", "providers", "--scope-kind", "user" });
        var providersCommand = ReflectionTestHelper.GetProperty(providersResult!, "Command");
        Assert.NotNull(providersCommand);
        var providersInvocation = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildRuntimeSurfaceInvocation", providersCommand!);
        Assert.NotNull(providersInvocation);
        Assert.Equal("memory/providers/list", ReflectionTestHelper.GetProperty(providersInvocation!, "Method"));
        using (var providersPayload = JsonDocument.Parse(JsonSerializer.Serialize(ReflectionTestHelper.GetProperty(providersInvocation, "Parameters"))))
        {
            Assert.Equal("user", providersPayload.RootElement.GetProperty("scopeKind").GetString());
        }

        var addResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[]
            {
                "memory",
                "add",
                "--payload-json",
                "{\"memorySpaceId\":{\"value\":\"memory-space-expansion-002\"},\"key\":\"pref.shell\",\"value\":\"pwsh\",\"confidence\":0.9}"
            });
        var addCommand = ReflectionTestHelper.GetProperty(addResult!, "Command");
        Assert.NotNull(addCommand);
        var addInvocation = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildRuntimeSurfaceInvocation", addCommand!);
        Assert.NotNull(addInvocation);
        Assert.Equal("memory/add", ReflectionTestHelper.GetProperty(addInvocation!, "Method"));
        using (var addPayload = JsonDocument.Parse(JsonSerializer.Serialize(ReflectionTestHelper.GetProperty(addInvocation, "Parameters"))))
        {
            Assert.Equal("memory-space-expansion-002", addPayload.RootElement.GetProperty("memorySpaceId").GetProperty("value").GetString());
            Assert.Equal("pref.shell", addPayload.RootElement.GetProperty("key").GetString());
            Assert.Equal("pwsh", addPayload.RootElement.GetProperty("value").GetString());
        }

        var bindResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[]
            {
                "memory",
                "bind-provider",
                "--payload-json",
                "{\"providerId\":\"provider-expansion\",\"memorySpaceId\":{\"value\":\"memory-space-expansion-003\"},\"mode\":\"readWrite\",\"allowedCapabilities\":10}"
            });
        var bindCommand = ReflectionTestHelper.GetProperty(bindResult!, "Command");
        Assert.NotNull(bindCommand);
        var bindInvocation = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildRuntimeSurfaceInvocation", bindCommand!);
        Assert.NotNull(bindInvocation);
        Assert.Equal("memory/provider/bind", ReflectionTestHelper.GetProperty(bindInvocation!, "Method"));

        var citationResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[]
            {
                "memory",
                "citation",
                "--payload-json",
                "{\"citation\":{\"entries\":[{\"memoryRecordId\":{\"value\":\"memory-record-expansion-001\"},\"memorySpaceId\":{\"value\":\"memory-space-expansion-004\"},\"key\":\"pref.shell\"}]}}"
            });
        var citationCommand = ReflectionTestHelper.GetProperty(citationResult!, "Command");
        Assert.NotNull(citationCommand);
        var citationInvocation = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildRuntimeSurfaceInvocation", citationCommand!);
        Assert.NotNull(citationInvocation);
        Assert.Equal("memory/citation/record", ReflectionTestHelper.GetProperty(citationInvocation!, "Method"));
        using (var citationPayload = JsonDocument.Parse(JsonSerializer.Serialize(ReflectionTestHelper.GetProperty(citationInvocation, "Parameters"))))
        {
            var entry = citationPayload.RootElement.GetProperty("citation").GetProperty("entries")[0];
            Assert.Equal("memory-record-expansion-001", entry.GetProperty("memoryRecordId").GetProperty("value").GetString());
            Assert.Equal("memory-space-expansion-004", entry.GetProperty("memorySpaceId").GetProperty("value").GetString());
            Assert.Equal("pref.shell", entry.GetProperty("key").GetString());
        }
    }

    [Fact]
    public void BuildRuntimeSurfaceInvocation_CatalogAndAgentFormalQueries_UseExpectedMethodsAndPayloads()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");

        var catalogResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "model-route", "catalog", "--cwd", "D:\\GitRepos\\Personal\\TianShu", "--limit", "12", "--include-hidden" });
        var catalogCommand = ReflectionTestHelper.GetProperty(catalogResult!, "Command");
        Assert.NotNull(catalogCommand);
        var catalogInvocation = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildRuntimeSurfaceInvocation", catalogCommand!);
        Assert.NotNull(catalogInvocation);
        Assert.Equal("model/catalog/read", ReflectionTestHelper.GetProperty(catalogInvocation!, "Method"));
        using (var catalogPayload = JsonDocument.Parse(JsonSerializer.Serialize(ReflectionTestHelper.GetProperty(catalogInvocation, "Parameters"))))
        {
            Assert.Equal("D:\\GitRepos\\Personal\\TianShu", catalogPayload.RootElement.GetProperty("cwd").GetString());
            Assert.Equal(12, catalogPayload.RootElement.GetProperty("limit").GetInt32());
            Assert.True(catalogPayload.RootElement.GetProperty("includeHidden").GetBoolean());
        }

        var resolveResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[]
            {
                "model-route",
                "resolve",
                "--provider-key",
                "openai",
                "--model-key",
                "gpt-5",
                "--reasoning-effort",
                "high",
                "--reasoning-summary",
                "detailed",
                "--verbosity",
                "verbose",
                "--prefer-websocket-transport",
            });
        var resolveCommand = ReflectionTestHelper.GetProperty(resolveResult!, "Command");
        Assert.NotNull(resolveCommand);
        var resolveInvocation = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildRuntimeSurfaceInvocation", resolveCommand!);
        Assert.NotNull(resolveInvocation);
        Assert.Equal("model/binding/resolve", ReflectionTestHelper.GetProperty(resolveInvocation!, "Method"));
        using (var resolvePayload = JsonDocument.Parse(JsonSerializer.Serialize(ReflectionTestHelper.GetProperty(resolveInvocation, "Parameters"))))
        {
            Assert.Equal("openai", resolvePayload.RootElement.GetProperty("providerKey").GetString());
            Assert.Equal("gpt-5", resolvePayload.RootElement.GetProperty("modelKey").GetString());
            Assert.Equal("high", resolvePayload.RootElement.GetProperty("reasoningEffort").GetString());
            Assert.Equal("detailed", resolvePayload.RootElement.GetProperty("reasoningSummary").GetString());
            Assert.Equal("verbose", resolvePayload.RootElement.GetProperty("verbosity").GetString());
            Assert.True(resolvePayload.RootElement.GetProperty("preferWebsocketTransport").GetBoolean());
        }

        var toolsResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "tools", "list", "--cwd", "D:\\GitRepos\\Personal\\TianShu", "--include-hidden" });
        var toolsCommand = ReflectionTestHelper.GetProperty(toolsResult!, "Command");
        Assert.NotNull(toolsCommand);
        var toolsInvocation = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildRuntimeSurfaceInvocation", toolsCommand!);
        Assert.NotNull(toolsInvocation);
        Assert.Equal("tools/catalog/read", ReflectionTestHelper.GetProperty(toolsInvocation!, "Method"));
        using (var toolsPayload = JsonDocument.Parse(JsonSerializer.Serialize(ReflectionTestHelper.GetProperty(toolsInvocation, "Parameters"))))
        {
            Assert.Equal("D:\\GitRepos\\Personal\\TianShu", toolsPayload.RootElement.GetProperty("cwd").GetString());
            Assert.True(toolsPayload.RootElement.GetProperty("includeHidden").GetBoolean());
        }

        var agentListResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "agent", "list", "--limit", "9", "--cursor", "agent-cursor-009", "--include-primary-threads" });
        var agentListCommand = ReflectionTestHelper.GetProperty(agentListResult!, "Command");
        Assert.NotNull(agentListCommand);
        var agentListInvocation = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildRuntimeSurfaceInvocation", agentListCommand!);
        Assert.NotNull(agentListInvocation);
        Assert.Equal("agent/list", ReflectionTestHelper.GetProperty(agentListInvocation!, "Method"));
        using var agentListPayload = JsonDocument.Parse(JsonSerializer.Serialize(ReflectionTestHelper.GetProperty(agentListInvocation, "Parameters")));
        Assert.Equal(9, agentListPayload.RootElement.GetProperty("limit").GetInt32());
        Assert.Equal("agent-cursor-009", agentListPayload.RootElement.GetProperty("cursor").GetString());
        Assert.True(agentListPayload.RootElement.GetProperty("includePrimaryThreads").GetBoolean());
    }

    [Fact]
    public void Parse_ReviewStartCustomCommand_SetsThreadId_And_Instructions()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "review", "start", "--thread-id", "thread_review_001", "--target", "custom", "--instructions", "请审查潜在风险", "--json" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("RuntimeSurfaceCommandOptions", command!.GetType().Name);
        Assert.Equal("ReviewStart", ReflectionTestHelper.GetProperty(command, "CommandKind")?.ToString());
        Assert.Equal("thread_review_001", ReflectionTestHelper.GetProperty(command, "ThreadId"));
        Assert.Equal("custom", ReflectionTestHelper.GetProperty(command, "ReviewTargetType"));
        Assert.Equal("请审查潜在风险", ReflectionTestHelper.GetProperty(command, "ReviewInstructions"));
        Assert.Equal(true, ReflectionTestHelper.GetProperty(command, "OutputJson"));
    }

    [Fact]
    public void BuildRuntimeSurfaceInvocation_ReviewStartCustom_UsesExpectedMethod()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");

        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "review", "start", "--thread-id", "thread_review_002", "--target", "custom", "--instructions", "检查回归风险" });
        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);

        var invocation = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildRuntimeSurfaceInvocation", command!);
        Assert.NotNull(invocation);
        Assert.Equal("review/start", ReflectionTestHelper.GetProperty(invocation!, "Method"));

        var payload = ReflectionTestHelper.GetProperty(invocation!, "Parameters");
        var json = JsonSerializer.Serialize(payload);
        using var document = JsonDocument.Parse(json);
        Assert.Equal("thread_review_002", document.RootElement.GetProperty("threadId").GetString());
        var target = document.RootElement.GetProperty("target");
        Assert.Equal("custom", target.GetProperty("type").GetString());
        Assert.Equal("检查回归风险", target.GetProperty("instructions").GetString());
    }

    [Fact]
    public void Parse_ReviewStartBaseBranchDetachedCommand_SetsDelivery_And_Branch()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "review", "start", "--thread-id", "thread_review_003", "--target", "base-branch", "--branch", "main", "--delivery", "detached" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("baseBranch", ReflectionTestHelper.GetProperty(command!, "ReviewTargetType"));
        Assert.Equal("main", ReflectionTestHelper.GetProperty(command, "ReviewBranch"));
        Assert.Equal("detached", ReflectionTestHelper.GetProperty(command, "Delivery"));
    }

    [Fact]
    public void BuildRuntimeSurfaceInvocation_ReviewStartCommit_UsesExpectedMethod()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");

        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "review", "start", "--thread-id", "thread_review_004", "--target", "commit", "--sha", "abc123def", "--title", "修复命令链路" });
        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);

        var invocation = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildRuntimeSurfaceInvocation", command!);
        Assert.NotNull(invocation);
        Assert.Equal("review/start", ReflectionTestHelper.GetProperty(invocation!, "Method"));

        var payload = ReflectionTestHelper.GetProperty(invocation!, "Parameters");
        var json = JsonSerializer.Serialize(payload);
        using var document = JsonDocument.Parse(json);
        Assert.Equal("thread_review_004", document.RootElement.GetProperty("threadId").GetString());
        var target = document.RootElement.GetProperty("target");
        Assert.Equal("commit", target.GetProperty("type").GetString());
        Assert.Equal("abc123def", target.GetProperty("sha").GetString());
        Assert.Equal("修复命令链路", target.GetProperty("title").GetString());
    }

    [Fact]
    public void Parse_ReviewStartCustomWithoutInstructions_ReturnsErrorMessage()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "review", "start", "--thread-id", "thread_review_005", "--target", "custom" });
        Assert.NotNull(result);

        var errorMessage = Assert.IsType<string>(ReflectionTestHelper.GetProperty(result!, "ErrorMessage"));
        Assert.Contains("custom review 需要 --instructions", errorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteSkillsListResult_PrintsRowsAndErrors()
    {
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var optionsType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.RuntimeSurfaceCommandOptions");
        var runner = Activator.CreateInstance(runnerType, nonPublic: true);
        var options = Activator.CreateInstance(optionsType);
        Assert.NotNull(runner);
        Assert.NotNull(options);

        using var writer = new StringWriter();
        var originalOut = Console.Out;
        try
        {
            Console.SetOut(writer);
            var exitCode = ReflectionTestHelper.InvokeMethod(
                runner!,
                "WriteSkillsListResult",
                options!,
                new ControlPlaneSkillCatalogResult
                {
                    Entries =
                    [
                        new ControlPlaneSkillCatalogEntry
                        {
                            WorkingDirectory = "D:/Work/TianShu",
                            Skills =
                            [
                                new ControlPlaneSkillDescriptor
                                {
                                    Scope = "repo",
                                    Name = "demo-search",
                                    Path = "D:/Work/TianShu/.tianshu/skills/demo-search/SKILL.md",
                                },
                            ],
                            Errors =
                            [
                                new ControlPlaneSkillError
                                {
                                    Path = "D:/Work/TianShu/.tianshu/skills/broken",
                                    Message = "manifest 缺少 name",
                                },
                            ],
                        },
                    ],
                });

            Assert.Equal(0, Assert.IsType<int>(exitCode));
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        var output = writer.ToString();
        Assert.Contains("[D:/Work/TianShu]", output, StringComparison.Ordinal);
        Assert.Contains("repo\tdemo-search\tD:/Work/TianShu/.tianshu/skills/demo-search/SKILL.md", output, StringComparison.Ordinal);
        Assert.Contains("! D:/Work/TianShu/.tianshu/skills/broken: manifest 缺少 name", output, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteSkillsConfigWriteResult_PrintsHumanReadableSummary()
    {
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var optionsType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.RuntimeSurfaceCommandOptions");
        var runner = Activator.CreateInstance(runnerType, nonPublic: true);
        var options = Activator.CreateInstance(optionsType);
        Assert.NotNull(runner);
        Assert.NotNull(options);
        ReflectionTestHelper.SetProperty(options!, "SkillPath", "D:/Work/TianShu/.tianshu/skills/demo-enable");

        using var writer = new StringWriter();
        var originalOut = Console.Out;
        try
        {
            Console.SetOut(writer);
            var exitCode = ReflectionTestHelper.InvokeMethod(
                runner!,
                "WriteSkillsConfigWriteResult",
                options!,
                new ControlPlaneSkillConfigWriteResult
                {
                    EffectiveEnabled = true,
                });

            Assert.Equal(0, Assert.IsType<int>(exitCode));
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        Assert.Contains("已启用技能：D:/Work/TianShu/.tianshu/skills/demo-enable", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void WriteSkillsRemoteListResult_PrintsRowsAndNextCursor()
    {
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var optionsType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.RuntimeSurfaceCommandOptions");
        var runner = Activator.CreateInstance(runnerType, nonPublic: true);
        var options = Activator.CreateInstance(optionsType);
        Assert.NotNull(runner);
        Assert.NotNull(options);

        using var writer = new StringWriter();
        var originalOut = Console.Out;
        try
        {
            Console.SetOut(writer);
            var exitCode = ReflectionTestHelper.InvokeMethod(
                runner!,
                "WriteSkillsRemoteListResult",
                options!,
                new ControlPlaneRemoteSkillCatalogResult
                {
                    Items =
                    [
                        new ControlPlaneRemoteSkillSummary
                        {
                            Id = "skill_remote_001",
                            Name = "Remote Search",
                            HazelnutScope = "org",
                        },
                    ],
                    NextCursor = "cursor_remote_001",
                });

            Assert.Equal(0, Assert.IsType<int>(exitCode));
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        var output = writer.ToString();
        Assert.Contains("skill_remote_001\tRemote Search\torg", output, StringComparison.Ordinal);
        Assert.Contains("nextCursor\tcursor_remote_001", output, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteSkillsRemoteExportResult_PrintsExportPath()
    {
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var optionsType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.RuntimeSurfaceCommandOptions");
        var runner = Activator.CreateInstance(runnerType, nonPublic: true);
        var options = Activator.CreateInstance(optionsType);
        Assert.NotNull(runner);
        Assert.NotNull(options);

        using var writer = new StringWriter();
        var originalOut = Console.Out;
        try
        {
            Console.SetOut(writer);
            var exitCode = ReflectionTestHelper.InvokeMethod(
                runner!,
                "WriteSkillsRemoteExportResult",
                options!,
                new ControlPlaneRemoteSkillExportResult
                {
                    Id = "skill_remote_001",
                    Path = "D:/Exports/skill_remote_001",
                });

            Assert.Equal(0, Assert.IsType<int>(exitCode));
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        Assert.Contains("远程技能已导出：D:/Exports/skill_remote_001", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void WritePluginListResult_PrintsRowsAndSyncError()
    {
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var optionsType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.RuntimeSurfaceCommandOptions");
        var runner = Activator.CreateInstance(runnerType, nonPublic: true);
        var options = Activator.CreateInstance(optionsType);
        Assert.NotNull(runner);
        Assert.NotNull(options);

        using var writer = new StringWriter();
        var originalOut = Console.Out;
        try
        {
            Console.SetOut(writer);
            var exitCode = ReflectionTestHelper.InvokeMethod(
                runner!,
                "WritePluginListResult",
                options!,
                new ControlPlanePluginCatalogResult
                {
                    Marketplaces =
                    [
                        new ControlPlanePluginMarketplace
                        {
                            Name = "debug",
                            Path = "D:/marketplace/marketplace.json",
                            Plugins =
                            [
                                new ControlPlanePluginSummary
                                {
                                    Name = "demo-plugin",
                                    Enabled = true,
                                    Source = StructuredValue.FromPlainObject(new Dictionary<string, object?>
                                    {
                                        ["type"] = "local",
                                        ["path"] = "D:/marketplace/demo-plugin",
                                    }),
                                },
                            ],
                        },
                    ],
                    RemoteSyncError = "sync timeout",
                });

            Assert.Equal(0, Assert.IsType<int>(exitCode));
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        var output = writer.ToString();
        Assert.Contains("[debug] D:/marketplace/marketplace.json", output, StringComparison.Ordinal);
        Assert.Contains("enabled\tdemo-plugin\tD:/marketplace/demo-plugin", output, StringComparison.Ordinal);
        Assert.Contains("remoteSyncError=sync timeout", output, StringComparison.Ordinal);
    }

    [Fact]
    public void WritePluginReadResult_PrintsTypedSummary()
    {
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var optionsType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.RuntimeSurfaceCommandOptions");
        var runner = Activator.CreateInstance(runnerType, nonPublic: true);
        var options = Activator.CreateInstance(optionsType);
        Assert.NotNull(runner);
        Assert.NotNull(options);

        using var writer = new StringWriter();
        var originalOut = Console.Out;
        try
        {
            Console.SetOut(writer);
            var exitCode = ReflectionTestHelper.InvokeMethod(
                runner!,
                "WritePluginReadResult",
                options!,
                new ControlPlanePluginReadResult
                {
                    Plugin = new ControlPlanePluginDetail
                    {
                        MarketplaceName = "debug",
                        MarketplacePath = "D:/marketplace/marketplace.json",
                        Summary = new ControlPlanePluginSummary
                        {
                            Id = "demo-plugin@debug",
                            Name = "demo-plugin",
                            Source = StructuredValue.FromPlainObject(new Dictionary<string, object?>
                            {
                                ["type"] = "local",
                                ["path"] = "D:/marketplace/demo-plugin",
                            }),
                            Installed = true,
                            Enabled = true,
                            InstallPolicy = "AVAILABLE",
                            AuthPolicy = "ON_INSTALL",
                        },
                        Description = "demo description",
                        Skills =
                        [
                            new ControlPlanePluginSkillReference
                            {
                                Name = "demo-plugin:search",
                                Description = "search skill",
                                Path = "D:/marketplace/demo-plugin/skills/search",
                            },
                        ],
                        Apps =
                        [
                            new ControlPlanePluginAppReference
                            {
                                Id = "connector_example",
                                Name = "connector_example",
                                InstallUrl = "https://chatgpt.com/apps/connector_example/connector_example",
                            },
                        ],
                        McpServers = ["demo"],
                    },
                });

            Assert.Equal(0, Assert.IsType<int>(exitCode));
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        var output = writer.ToString();
        Assert.Contains("插件：demo-plugin", output, StringComparison.Ordinal);
        Assert.Contains("键：demo-plugin@debug", output, StringComparison.Ordinal);
        Assert.Contains("来源：local\tD:/marketplace/demo-plugin", output, StringComparison.Ordinal);
        Assert.Contains("技能数：1", output, StringComparison.Ordinal);
        Assert.Contains("应用数：1", output, StringComparison.Ordinal);
        Assert.Contains("MCP Server 数：1", output, StringComparison.Ordinal);
    }

    [Fact]
    public void WritePluginInstallResult_PrintsPendingAuthApps()
    {
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var optionsType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.RuntimeSurfaceCommandOptions");
        var runner = Activator.CreateInstance(runnerType, nonPublic: true);
        var options = Activator.CreateInstance(optionsType);
        Assert.NotNull(runner);
        Assert.NotNull(options);

        using var writer = new StringWriter();
        var originalOut = Console.Out;
        try
        {
            Console.SetOut(writer);
            var exitCode = ReflectionTestHelper.InvokeMethod(
                runner!,
                "WritePluginInstallResult",
                options!,
                new ControlPlanePluginInstallResult
                {
                    AuthPolicy = "ON_INSTALL",
                    AppsNeedingAuth =
                    [
                        new ControlPlanePluginAppReference
                        {
                            Id = "connector_example",
                            Name = "connector_example",
                            InstallUrl = "https://chatgpt.com/apps/connector_example/connector_example",
                        },
                    ],
                });

            Assert.Equal(0, Assert.IsType<int>(exitCode));
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        var output = writer.ToString();
        Assert.Contains("插件安装请求已完成。", output, StringComparison.Ordinal);
        Assert.Contains("以下应用仍需授权：", output, StringComparison.Ordinal);
        Assert.Contains("connector_example\tconnector_example\thttps://chatgpt.com/apps/connector_example/connector_example", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteReviewStartResult_PrintsHumanReadableSummary()
    {
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var optionsType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.RuntimeSurfaceCommandOptions");
        var runner = Activator.CreateInstance(runnerType, nonPublic: true);
        var options = Activator.CreateInstance(optionsType);
        Assert.NotNull(runner);
        Assert.NotNull(options);

        using var writer = new StringWriter();
        var originalOut = Console.Out;
        try
        {
            Console.SetOut(writer);
            var exitCode = ReflectionTestHelper.InvokeMethod(
                runner!,
                "WriteReviewStartResult",
                options!,
                new ControlPlaneReviewStartResult
                {
                    ReviewThreadId = "review_thread_001",
                    Turn = new ControlPlaneReviewTurn
                    {
                        Id = "turn_review_001",
                        Status = "inProgress",
                        DisplayText = "请审查当前改动中的回归风险。",
                    },
                });

            Assert.Equal(0, Assert.IsType<int>(exitCode));
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        var output = writer.ToString();
        Assert.Contains("已启动 review。", output, StringComparison.Ordinal);
        Assert.Contains("reviewThreadId：review_thread_001", output, StringComparison.Ordinal);
        Assert.Contains("turnId：turn_review_001", output, StringComparison.Ordinal);
        Assert.Contains("请求：请审查当前改动中的回归风险。", output, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteExperimentalFeatureListResult_PrintsRowsAndNextCursor()
    {
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var optionsType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.RuntimeSurfaceCommandOptions");
        var runner = Activator.CreateInstance(runnerType, nonPublic: true);
        var options = Activator.CreateInstance(optionsType);
        Assert.NotNull(runner);
        Assert.NotNull(options);

        using var writer = new StringWriter();
        var originalOut = Console.Out;
        try
        {
            Console.SetOut(writer);
            var exitCode = ReflectionTestHelper.InvokeMethod(
                runner!,
                "WriteExperimentalFeatureListResult",
                options!,
                new ControlPlaneExperimentalFeatureCatalogResult
                {
                    NextCursor = "cursor_02",
                    Items =
                    [
                        new ControlPlaneExperimentalFeatureDescriptor
                        {
                            Name = "tool_search",
                            Stage = "beta",
                            DisplayName = "Tool Search",
                            Enabled = true,
                            DefaultEnabled = false,
                        },
                    ],
                });

            Assert.Equal(0, Assert.IsType<int>(exitCode));
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        var output = writer.ToString();
        Assert.Contains("tool_search\tbeta\tenabled=True\tdefault=False\tTool Search", output, StringComparison.Ordinal);
        Assert.Contains("nextCursor\tcursor_02", output, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteFeatureListResult_SortsRowsAndPrintsTianShuStyleColumns()
    {
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var optionsType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.RuntimeSurfaceCommandOptions");
        var runner = Activator.CreateInstance(runnerType, nonPublic: true);
        var options = Activator.CreateInstance(optionsType);
        Assert.NotNull(runner);
        Assert.NotNull(options);
        ReflectionTestHelper.SetProperty(options!, "CommandKind", Enum.Parse(ReflectionTestHelper.GetProperty(options!, "CommandKind")!.GetType(), "FeatureList"));

        using var writer = new StringWriter();
        var originalOut = Console.Out;
        try
        {
            Console.SetOut(writer);
            var exitCode = ReflectionTestHelper.InvokeMethod(
                runner!,
                "WriteFeatureListResult",
                options!,
                new ControlPlaneExperimentalFeatureCatalogResult
                {
                    Items =
                    [
                        new ControlPlaneExperimentalFeatureDescriptor
                        {
                            Name = "zeta",
                            Stage = "underDevelopment",
                            Enabled = true,
                        },
                        new ControlPlaneExperimentalFeatureDescriptor
                        {
                            Name = "alpha",
                            Stage = "beta",
                            Enabled = false,
                        },
                    ],
                });

            Assert.Equal(0, Assert.IsType<int>(exitCode));
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        var lines = writer.ToString()
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal(2, lines.Length);
        Assert.StartsWith("alpha", lines[0], StringComparison.Ordinal);
        Assert.Contains("experimental", lines[0], StringComparison.Ordinal);
        Assert.EndsWith("False", lines[0], StringComparison.Ordinal);
        Assert.StartsWith("zeta", lines[1], StringComparison.Ordinal);
        Assert.Contains("under development", lines[1], StringComparison.Ordinal);
        Assert.EndsWith("True", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void WriteCollaborationModeListResult_PrintsRows()
    {
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var optionsType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.RuntimeSurfaceCommandOptions");
        var runner = Activator.CreateInstance(runnerType, nonPublic: true);
        var options = Activator.CreateInstance(optionsType);
        Assert.NotNull(runner);
        Assert.NotNull(options);

        using var writer = new StringWriter();
        var originalOut = Console.Out;
        try
        {
            Console.SetOut(writer);
            var exitCode = ReflectionTestHelper.InvokeMethod(
                runner!,
                "WriteCollaborationModeListResult",
                options!,
                new ControlPlaneCollaborationModeCatalogResult
                {
                    Items =
                    [
                        new ControlPlaneCollaborationModeDescriptor
                        {
                            Name = "plan",
                            Mode = "plan",
                            Model = "gpt-5.4",
                            ReasoningEffort = "high",
                        },
                    ],
                });

            Assert.Equal(0, Assert.IsType<int>(exitCode));
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        Assert.Contains("plan\tplan\tgpt-5.4\thigh", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void WriteMcpServerStatusListResult_PrintsRowsAndNextCursor()
    {
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var optionsType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.RuntimeSurfaceCommandOptions");
        var runner = Activator.CreateInstance(runnerType, nonPublic: true);
        var options = Activator.CreateInstance(optionsType);
        Assert.NotNull(runner);
        Assert.NotNull(options);

        using var writer = new StringWriter();
        var originalOut = Console.Out;
        try
        {
            Console.SetOut(writer);
            var exitCode = ReflectionTestHelper.InvokeMethod(
                runner!,
                "WriteMcpServerStatusListResult",
                options!,
                new ControlPlaneMcpServerCatalogResult
                {
                    NextCursor = "cursor_mcp_02",
                    Items =
                    [
                        new ControlPlaneMcpServerDescriptor
                        {
                            Name = "demo",
                            AuthStatus = "authorized",
                            ToolNames = ["search", "read"],
                            ResourceUris = ["resource://demo/index"],
                            ResourceTemplateUris = ["resource://demo/{id}"],
                        },
                    ],
                });

            Assert.Equal(0, Assert.IsType<int>(exitCode));
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        var output = writer.ToString();
        Assert.Contains("demo\tauth=authorized\ttools=2\tresources=1\ttemplates=1", output, StringComparison.Ordinal);
        Assert.Contains("nextCursor\tcursor_mcp_02", output, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteMcpServerReloadResult_PrintsHumanReadableSummary()
    {
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var optionsType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.RuntimeSurfaceCommandOptions");
        var runner = Activator.CreateInstance(runnerType, nonPublic: true);
        var options = Activator.CreateInstance(optionsType);
        Assert.NotNull(runner);
        Assert.NotNull(options);

        using var writer = new StringWriter();
        var originalOut = Console.Out;
        try
        {
            Console.SetOut(writer);
            var exitCode = ReflectionTestHelper.InvokeMethod(
                runner!,
                "WriteMcpServerReloadResult",
                options!,
                new ControlPlaneMcpServerReloadResult());

            Assert.Equal(0, Assert.IsType<int>(exitCode));
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        Assert.Contains("MCP Server 已重新加载。", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void WriteThreadOperationResult_PrintsThreadSummary()
    {
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var runner = Activator.CreateInstance(runnerType, nonPublic: true);
        Assert.NotNull(runner);

        using var writer = new StringWriter();
        var originalOut = Console.Out;
        try
        {
            Console.SetOut(writer);
            var exitCode = ReflectionTestHelper.InvokeMethod(
                runner!,
                "WriteThreadOperationResult",
                new ControlPlaneThreadOperationResult
                {
                    Thread = new ControlPlaneThreadDetail
                    {
                        ThreadId = new ThreadId("thread_cli_001"),
                        WorkingDirectory = "D:/Work/TianShu",
                        Preview = "当前线程预览",
                        Turns =
                        [
                            new ControlPlaneThreadTurn { Id = "turn_1", Status = "completed" },
                            new ControlPlaneThreadTurn { Id = "turn_2", Status = "completed" },
                        ],
                    },
                },
                false,
                "已读取线程。",
                "读取线程失败。");

            Assert.Equal(0, Assert.IsType<int>(exitCode));
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        var output = writer.ToString();
        Assert.Contains("已读取线程。", output, StringComparison.Ordinal);
        Assert.Contains("线程：thread_cli_001", output, StringComparison.Ordinal);
        Assert.Contains("工作目录：D:/Work/TianShu", output, StringComparison.Ordinal);
        Assert.Contains("标题：当前线程预览", output, StringComparison.Ordinal);
        Assert.Contains("轮次：2", output, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteAgentThreadRegistrationResult_PrintsThreadSummary()
    {
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var runner = Activator.CreateInstance(runnerType, nonPublic: true);
        Assert.NotNull(runner);

        using var writer = new StringWriter();
        var originalOut = Console.Out;
        try
        {
            Console.SetOut(writer);
            var exitCode = ReflectionTestHelper.InvokeMethod(
                runner!,
                "WriteAgentThreadRegistrationResult",
                new ControlPlaneAgentThreadRegistrationResult
                {
                    Agent = new ControlPlaneAgentDescriptor
                    {
                        ThreadId = new ThreadId("thread_cli_agent_001"),
                        WorkingDirectory = "D:/Work/TianShu",
                        Preview = "登记后的线程预览",
                    },
                },
                false,
                "已登记线程 Agent 元数据。",
                "登记线程 Agent 元数据失败。");

            Assert.Equal(0, Assert.IsType<int>(exitCode));
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        var output = writer.ToString();
        Assert.Contains("已登记线程 Agent 元数据。", output, StringComparison.Ordinal);
        Assert.Contains("线程：thread_cli_agent_001", output, StringComparison.Ordinal);
        Assert.Contains("工作目录：D:/Work/TianShu", output, StringComparison.Ordinal);
        Assert.Contains("标题：登记后的线程预览", output, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteConversationSummaryResult_PrintsConversationHeadline()
    {
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var optionsType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.RuntimeSurfaceCommandOptions");
        var runner = Activator.CreateInstance(runnerType, nonPublic: true);
        var options = Activator.CreateInstance(optionsType);
        Assert.NotNull(runner);
        Assert.NotNull(options);

        using var writer = new StringWriter();
        var originalOut = Console.Out;
        try
        {
            Console.SetOut(writer);
            var exitCode = ReflectionTestHelper.InvokeMethod(
                runner!,
                "WriteConversationSummaryResult",
                options!,
                new ControlPlaneConversationArtifact
                {
                    ConversationId = "conv_001",
                    Source = "rollout",
                    Path = "Test/.tianshu-probe/summary.json",
                    WorkingDirectory = "D:/Work/TianShu/Test",
                    UpdatedAt = "2026-03-11T10:00:00Z",
                    Preview = "探针闭环验证通过。",
                });

            Assert.Equal(0, Assert.IsType<int>(exitCode));
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        var output = writer.ToString();
        Assert.Contains("会话：conv_001", output, StringComparison.Ordinal);
        Assert.Contains("来源：rollout", output, StringComparison.Ordinal);
        Assert.Contains("路径：Test/.tianshu-probe/summary.json", output, StringComparison.Ordinal);
        Assert.Contains("摘要：探针闭环验证通过。", output, StringComparison.Ordinal);
    }


    [Fact]
    public void Parse_FeedbackUploadCommand_SetsClassification_Reason_And_ExtraLogs()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        using var tempDir = new TestTempDirectory();
        var logA = Path.Combine(tempDir.Path, "feedback-a.log");
        var logB = Path.Combine(tempDir.Path, "feedback-b.log");
        File.WriteAllText(logA, "a");
        File.WriteAllText(logB, "b");

        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[]
            {
                "feedback",
                "upload",
                "--classification",
                "bug",
                "--include-logs",
                "--thread-id",
                "thread_feedback_001",
                "--reason",
                "user supplied reason",
                "--extra-log-file",
                logA,
                "--extra-log-file",
                logB,
                "--json",
            });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("FeedbackCommandOptions", command!.GetType().Name);
        Assert.Equal("Upload", ReflectionTestHelper.GetProperty(command, "CommandKind")?.ToString());
        Assert.Equal("bug", ReflectionTestHelper.GetProperty(command, "Classification"));
        Assert.Equal(true, ReflectionTestHelper.GetProperty(command, "IncludeLogs"));
        Assert.Equal("thread_feedback_001", ReflectionTestHelper.GetProperty(command, "ThreadId"));
        Assert.Equal("user supplied reason", ReflectionTestHelper.GetProperty(command, "Reason"));
        Assert.Equal(true, ReflectionTestHelper.GetProperty(command, "OutputJson"));

        var extraLogFiles = Assert.IsAssignableFrom<System.Collections.IEnumerable>(ReflectionTestHelper.GetProperty(command, "ExtraLogFiles"));
        Assert.Equal(new[] { logA, logB }, extraLogFiles.Cast<object>().Select(static item => item.ToString()).ToArray());
    }

    [Fact]
    public void BuildFeedbackInvocation_UsesExpectedMethod_AndPayload()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        using var tempDir = new TestTempDirectory();
        var logPath = Path.Combine(tempDir.Path, "feedback.log");
        File.WriteAllText(logPath, "payload");

        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[]
            {
                "feedback",
                "upload",
                "--classification",
                "bug",
                "--include-logs",
                "--thread-id",
                "thread_feedback_002",
                "--reason",
                "feedback payload",
                "--extra-log-file",
                logPath,
            });
        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);

        var invocation = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildFeedbackInvocation", command!);
        Assert.NotNull(invocation);
        Assert.Equal("feedback/upload", ReflectionTestHelper.GetProperty(invocation!, "Method"));

        var payload = ReflectionTestHelper.GetProperty(invocation!, "Parameters");
        var json = JsonSerializer.Serialize(payload);
        using var document = JsonDocument.Parse(json);
        Assert.Equal("bug", document.RootElement.GetProperty("classification").GetString());
        Assert.True(document.RootElement.GetProperty("includeLogs").GetBoolean());
        Assert.Equal("thread_feedback_002", document.RootElement.GetProperty("threadId").GetString());
        Assert.Equal("feedback payload", document.RootElement.GetProperty("reason").GetString());
        Assert.Equal(logPath, document.RootElement.GetProperty("extraLogFiles")[0].GetString());
    }

    [Fact]
    public void Parse_WindowsSandboxSetupStartCommand_SetsMode_And_Cwd()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        using var tempDir = new TestTempDirectory();

        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "windows-sandbox", "setup-start", "--mode", "elevated", "--cwd", tempDir.Path, "--json" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("WindowsSandboxCommandOptions", command!.GetType().Name);
        Assert.Equal("SetupStart", ReflectionTestHelper.GetProperty(command, "CommandKind")?.ToString());
        Assert.Equal("elevated", ReflectionTestHelper.GetProperty(command, "Mode"));
        Assert.Equal(tempDir.Path, ReflectionTestHelper.GetProperty(command, "SandboxCwd"));
        Assert.Equal(true, ReflectionTestHelper.GetProperty(command, "OutputJson"));
    }

    [Fact]
    public void BuildWindowsSandboxInvocation_UsesExpectedMethod_AndPayload()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        using var tempDir = new TestTempDirectory();

        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "windows-sandbox", "setup-start", "--mode", "unelevated", "--cwd", tempDir.Path });
        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);

        var invocation = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildWindowsSandboxInvocation", command!);
        Assert.NotNull(invocation);
        Assert.Equal("windowsSandbox/setupStart", ReflectionTestHelper.GetProperty(invocation!, "Method"));

        var payload = ReflectionTestHelper.GetProperty(invocation!, "Parameters");
        var json = JsonSerializer.Serialize(payload);
        using var document = JsonDocument.Parse(json);
        Assert.Equal("unelevated", document.RootElement.GetProperty("mode").GetString());
        Assert.Equal(tempDir.Path, document.RootElement.GetProperty("cwd").GetString());
    }

    [Fact]
    public void Parse_RealtimeStartCommand_SetsThread_Session_And_Prompt()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "realtime", "start", "--thread-id", "thread_rt_001", "--session-id", "session_rt_001", "--prompt", "start prompt", "--json" });
        Assert.NotNull(result);

        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);
        Assert.Equal("RealtimeCommandOptions", command!.GetType().Name);
        Assert.Equal("Start", ReflectionTestHelper.GetProperty(command, "CommandKind")?.ToString());
        Assert.Equal("thread_rt_001", ReflectionTestHelper.GetProperty(command, "ThreadId"));
        Assert.Equal("session_rt_001", ReflectionTestHelper.GetProperty(command, "SessionId"));
        Assert.Equal("start prompt", ReflectionTestHelper.GetProperty(command, "Prompt"));
        Assert.Equal(true, ReflectionTestHelper.GetProperty(command, "OutputJson"));
    }

    [Fact]
    public void BuildRealtimeAppendTextRequest_UsesExpectedPayload()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "realtime", "append-text", "--thread-id", "thread_rt_002", "--session-id", "session_rt_002", "--text", "hello realtime" });
        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);

        var request = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildRealtimeAppendTextRequest", command!);
        Assert.NotNull(request);
        Assert.Equal("thread_rt_002", ReflectionTestHelper.GetProperty(ReflectionTestHelper.GetProperty(request!, "ThreadId")!, "Value"));
        Assert.Equal("session_rt_002", ReflectionTestHelper.GetProperty(request, "SessionId"));
        Assert.Equal("hello realtime", ReflectionTestHelper.GetProperty(request, "Text"));
    }

    [Fact]
    public void BuildRealtimeAppendAudioRequest_ReadsAudioFilePayload()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        using var tempDir = new TestTempDirectory();
        var audioPath = Path.Combine(tempDir.Path, "audio.json");
        File.WriteAllText(
            audioPath,
            """
            {
              "data": "AQIDBA==",
              "sampleRate": 24000,
              "numChannels": 1
            }
            """);

        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "realtime", "append-audio", "--thread-id", "thread_rt_003", "--audio-file", audioPath });
        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);

        var request = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildRealtimeAppendAudioRequest", command!);
        Assert.NotNull(request);
        Assert.Equal("thread_rt_003", ReflectionTestHelper.GetProperty(ReflectionTestHelper.GetProperty(request!, "ThreadId")!, "Value"));
        var audio = ReflectionTestHelper.GetProperty(request, "Audio");
        Assert.NotNull(audio);
        Assert.Equal("AQIDBA==", ReflectionTestHelper.GetProperty(audio!, "Data"));
        Assert.Equal(24000, ReflectionTestHelper.GetProperty(audio, "SampleRate"));
        Assert.Equal(1, ReflectionTestHelper.GetProperty(audio, "NumChannels"));
    }

    [Fact]
    public void BuildRealtimeHandoffOutputRequest_UsesExpectedPayload()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "realtime", "handoff-output", "--thread-id", "thread_rt_004", "--session-id", "session_rt_004", "--handoff-id", "call_rt_004", "--output", "delegated result" });
        var command = ReflectionTestHelper.GetProperty(result!, "Command");
        Assert.NotNull(command);

        var request = ReflectionTestHelper.InvokeStaticMethod(runnerType, "BuildRealtimeHandoffOutputRequest", command!);
        Assert.NotNull(request);
        Assert.Equal("thread_rt_004", ReflectionTestHelper.GetProperty(ReflectionTestHelper.GetProperty(request!, "ThreadId")!, "Value"));
        Assert.Equal("session_rt_004", ReflectionTestHelper.GetProperty(request, "SessionId"));
        Assert.Equal("call_rt_004", ReflectionTestHelper.GetProperty(request, "HandoffId"));
        Assert.Equal("delegated result", ReflectionTestHelper.GetProperty(request, "Output"));
    }


    [Fact]
    public void Parse_FeedbackUpload_WithOptionLikeClassificationValue_ReturnsError()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "feedback", "upload", "--classification", "--json" });
        Assert.NotNull(result);

        var errorMessage = Assert.IsType<string>(ReflectionTestHelper.GetProperty(result!, "ErrorMessage"));
        Assert.Contains("--classification", errorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_WindowsSandboxSetupStart_ExpandsEnvironmentVariableCwd()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        using var tempDir = new TestTempDirectory();
        const string variableName = "TIANSHU_SANDBOX_TEST_DIR";
        var originalValue = Environment.GetEnvironmentVariable(variableName);

        try
        {
            Environment.SetEnvironmentVariable(variableName, tempDir.Path);
            var result = ReflectionTestHelper.InvokeStaticMethod(
                parserType,
                "Parse",
                (object)new[] { "windows-sandbox", "setup-start", "--mode", "elevated", "--cwd", $"%{variableName}%" });
            Assert.NotNull(result);

            var command = ReflectionTestHelper.GetProperty(result!, "Command");
            Assert.NotNull(command);
            Assert.Equal(tempDir.Path, ReflectionTestHelper.GetProperty(command!, "SandboxCwd"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(variableName, originalValue);
        }
    }

    [Fact]
    public void Parse_RealtimeAppendAudio_WithoutPayload_ReturnsError()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "realtime", "append-audio", "--thread-id", "thread_rt_missing_audio" });
        Assert.NotNull(result);

        var errorMessage = Assert.IsType<string>(ReflectionTestHelper.GetProperty(result!, "ErrorMessage"));
        Assert.Contains("append-audio", errorMessage, StringComparison.Ordinal);
        Assert.Contains("--audio-json", errorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_RealtimeAppendAudio_WithOptionLikeAudioFileValue_ReturnsError()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "realtime", "append-audio", "--thread-id", "thread_rt_bad_audio", "--audio-file", "--json" });
        Assert.NotNull(result);

        var errorMessage = Assert.IsType<string>(ReflectionTestHelper.GetProperty(result!, "ErrorMessage"));
        Assert.Contains("--audio-file", errorMessage, StringComparison.Ordinal);
    }


    [Fact]
    public void ChatScriptCommandFile_Load_IgnoresComments_And_BlankLines()
    {
        var scriptType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.ChatScriptCommandFile");
        using var tempDir = new TestTempDirectory();
        var scriptPath = Path.Combine(tempDir.Path, "chat-script.txt");
        File.WriteAllText(
            scriptPath,
            string.Join(Environment.NewLine, new[]
            {
                "# comment",
                string.Empty,
                "  你好  ",
                "// hidden",
                "/wait 10",
            }));

        var script = ReflectionTestHelper.InvokeStaticMethod(scriptType, "Load", scriptPath);
        Assert.NotNull(script);
        Assert.Equal(scriptPath, ReflectionTestHelper.GetProperty(script!, "Path"));

        var commands = Assert.IsAssignableFrom<System.Collections.IEnumerable>(ReflectionTestHelper.GetProperty(script!, "Commands"));
        var items = commands.Cast<object>().Select(static item => item.ToString()).ToArray();
        Assert.Equal(new[] { "你好", "/wait 10" }, items);
    }

    [Fact]
    public void Parse_SkillsRemoteExportWithoutHazelnutId_ReturnsError()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "skills", "remote-export" });
        Assert.NotNull(result);

        var errorMessage = Assert.IsType<string>(ReflectionTestHelper.GetProperty(result!, "ErrorMessage"));
        Assert.Contains("--hazelnut-id", errorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_McpAddWithoutTransport_ReturnsError()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var result = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "mcp", "add", "demo" });
        Assert.NotNull(result);

        var errorMessage = Assert.IsType<string>(ReflectionTestHelper.GetProperty(result!, "ErrorMessage"));
        Assert.Contains("--url", errorMessage, StringComparison.Ordinal);
    }

    private static object CreateChatOptions()
    {
        var optionsType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.ChatCommandOptions");
        return Activator.CreateInstance(optionsType)!;
    }

    private static string WriteStartupBannerConfig(
        string rootPath,
        string model,
        string provider,
        string defaultProtocol)
    {
        var configDirectory = Path.Combine(rootPath, ".tianshu");
        Directory.CreateDirectory(configDirectory);
        var configPath = Path.Combine(configDirectory, "tianshu.toml");
        File.WriteAllText(
            configPath,
            $$"""
            model = "{{model}}"
            provider = "{{provider}}"
            approval_policy = "on-request"
            sandbox_mode = "workspace-write"

            [providers.{{provider}}]
            base_url = "https://example.invalid"
            api_key_env = "TIANSHU_TEST_API_KEY"
            default_protocol = "{{defaultProtocol}}"
            """);
        return configPath;
    }

    private static bool ShouldUseTerminalChatTui(
        Type runnerType,
        object options,
        bool hasScript,
        bool isInputRedirected,
        bool isOutputRedirected)
        => Assert.IsType<bool>(ReflectionTestHelper.InvokeStaticMethod(
            runnerType,
            "ShouldUseTerminalChatTui",
            options,
            hasScript,
            isInputRedirected,
            isOutputRedirected));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TianShu.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Unable to locate TianShu repository root.");
    }
}
