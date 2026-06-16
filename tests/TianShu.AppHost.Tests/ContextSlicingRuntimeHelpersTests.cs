using System.Text.Encodings.Web;
using System.Text.Json;
using TianShu.AppHost.Tools.Runtime;
using TianShu.Execution.Runtime;

namespace TianShu.AppHost.Tests;

public sealed class ContextSlicingRuntimeHelpersTests
{
    [Fact]
    public void ResolveConfiguredBudgetProfile_ShouldReadNestedContextBudgetKeys()
    {
        var config = new Dictionary<string, object?>
        {
            ["context"] = new Dictionary<string, object?>
            {
                ["default_budget_tokens"] = 12000L,
                ["expanded_budget_tokens"] = "24000",
                ["safety_margin_tokens"] = 1000,
                ["memory_overlay_budget_tokens"] = 900,
                ["tool_output_budget_tokens"] = 800,
                ["history_raw_turn_budget_tokens"] = 700,
                ["summary_budget_tokens"] = 600,
            },
        };

        var profile = ContextSlicingRuntimeHelpers.ResolveConfiguredBudgetProfile(config);

        Assert.Equal(12000, profile.SoftBudgetTokens);
        Assert.Equal(24000, profile.ExpandedBudgetTokens);
        Assert.Equal(1000, profile.SafetyMarginTokens);
        Assert.Equal(900, profile.MemoryOverlayBudgetTokens);
        Assert.Equal(800, profile.ToolOutputBudgetTokens);
        Assert.Equal(700, profile.RecentTurnsBudgetTokens);
        Assert.Equal(600, profile.SummaryBudgetTokens);
    }

    [Fact]
    public void ResolveConfiguredBudgetProfile_ShouldIgnoreCamelCaseBudgetKeys()
    {
        var config = new Dictionary<string, object?>
        {
            ["context"] = new Dictionary<string, object?>
            {
                ["expandedBudgetTokens"] = "24000",
            },
        };

        var profile = ContextSlicingRuntimeHelpers.ResolveConfiguredBudgetProfile(config);

        Assert.Equal(ContextBudgetProfile.Default.ExpandedBudgetTokens, profile.ExpandedBudgetTokens);
    }

    [Fact]
    public void BuildDiagnosticPayload_ShouldExposeCountsWithoutSegmentText()
    {
        var report = new ContextSlicingReport
        {
            ThreadId = "thread-1",
            TurnId = "turn-1",
            TianShuBudgetTokens = 20,
            EstimatedTotalTokens = 40,
            EstimatedIncludedTokens = 20,
            IncludedSegments =
            [
                new ContextSegmentReportEntry(
                    "user",
                    ContextSegmentKind.CurrentUserInput,
                    ContextSegmentPriority.Critical,
                    20,
                    null,
                    [new ContextSourceRef(ContextSourceKind.UserInput, Id: "user")]),
            ],
            DroppedSegments =
            [
                new ContextSegmentReportEntry(
                    "secret-ish-history",
                    ContextSegmentKind.RecentTurn,
                    ContextSegmentPriority.Low,
                    20,
                    DroppedContextReason.BudgetExceeded,
                    []),
            ],
        };

        var payload = ContextSlicingRuntimeHelpers.BuildDiagnosticPayload(report, 1, "gpt-test", "openai");
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

        Assert.Contains("secret-ish-history", json, StringComparison.Ordinal);
        Assert.Contains("BudgetExceeded", json, StringComparison.Ordinal);
        Assert.DoesNotContain("用户原文", json, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildSlicedResponsesConversationInput_ShouldMaterializeMemoryOverlayBeforeCurrentUser()
    {
        var overlay = ContextSlicingRuntimeHelpers.CreateMemoryOverlaySegment(
            "memory-overlay",
            "用户偏好：不要覆盖 tianshu.toml。",
            "memory-1",
            "repo:tianshu");

        var sliced = KernelTurnExecutionRuntimeHelpers.BuildSlicedResponsesConversationInput(
            thread: null,
            userText: "继续当前任务",
            developerInstructions: "开发者指令",
            contextualUserMessages: null,
            currentInputItems: null,
            budgetProfile: new ContextBudgetProfile { SoftBudgetTokens = 10_000, SafetyMarginTokens = 0 },
            overlaySegments: [overlay]);

        var serialized = sliced.Input.Select(static item => JsonSerializer.Serialize(
            item,
            new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping })).ToArray();

        Assert.Equal(3, serialized.Length);
        Assert.Contains("Memory overlay", serialized[1], StringComparison.Ordinal);
        Assert.Contains("不要覆盖 tianshu.toml", serialized[1], StringComparison.Ordinal);
        Assert.Contains("继续当前任务", serialized[^1], StringComparison.Ordinal);
        Assert.Contains(sliced.Report.IncludedSegments, static item => item.Kind == ContextSegmentKind.MemoryOverlay);
    }

    [Fact]
    public void BuildSlicedResponsesConversationInput_ShouldMaterializeReferenceOnlyOverflowPlaceholder()
    {
        var artifact = new ContextSegment
        {
            Id = "artifact-large-diff",
            Kind = ContextSegmentKind.ArtifactSnippet,
            Priority = ContextSegmentPriority.Medium,
            RetentionPolicy = ContextRetentionPolicy.ReferenceOnlyIfDropped,
            Text = string.Concat(Enumerable.Repeat("large diff ", 200)),
            EstimatedTokens = 2_000,
            SourceRefs = [new ContextSourceRef(ContextSourceKind.Artifact, Id: "artifact-1", Path: "src/Foo.cs")],
        };

        var sliced = KernelTurnExecutionRuntimeHelpers.BuildSlicedResponsesConversationInput(
            thread: null,
            userText: "请审查当前变更",
            developerInstructions: "开发者指令",
            contextualUserMessages: null,
            currentInputItems: null,
            budgetProfile: new ContextBudgetProfile { SoftBudgetTokens = 10, SafetyMarginTokens = 0 },
            overlaySegments: [artifact]);

        var serialized = sliced.Input.Select(static item => JsonSerializer.Serialize(
            item,
            new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping })).ToArray();

        Assert.Contains(sliced.Report.ReferenceOnlySegments, static item => item.SegmentId == "artifact-large-diff");
        Assert.Contains(serialized, static item => item.Contains("Reference-only overflow segments", StringComparison.Ordinal));
        Assert.Contains(serialized, static item => item.Contains("artifact-large-diff", StringComparison.Ordinal));
        Assert.Contains(serialized, static item => item.Contains("src/Foo.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildSlicedResponsesFollowUpInput_ShouldRouteFunctionCallOutputAsToolResultSegment()
    {
        var longOutput = string.Concat(Enumerable.Repeat("0123456789 ", 1000));
        var sliced = KernelTurnExecutionRuntimeHelpers.BuildSlicedResponsesFollowUpInput(
            priorInput:
            [
                KernelTurnExecutionRuntimeHelpers.CreateResponsesMessage("user", "input_text", "run tool"),
            ],
            responseItems: [],
            nextInput:
            [
                new Dictionary<string, object?>
                {
                    ["type"] = "function_call_output",
                    ["call_id"] = "call-1",
                    ["output"] = longOutput,
                },
            ],
            threadId: "thread-1",
            turnId: "turn-1",
            budgetProfile: new ContextBudgetProfile { SoftBudgetTokens = 10_000, SafetyMarginTokens = 0 });

        var toolOutput = Assert.IsType<Dictionary<string, object?>>(sliced.Input[^1]);

        Assert.Equal("function_call_output", toolOutput["type"]);
        Assert.Contains("omitted", Assert.IsType<string>(toolOutput["output"]), StringComparison.Ordinal);
        Assert.Contains(sliced.Report.IncludedSegments, static item => item.Kind == ContextSegmentKind.ToolResult);
    }

    [Fact]
    public void BuildSlicedResponsesFollowUpInput_WhenToolResultIsKept_ShouldRestoreMatchingFunctionCall()
    {
        var hugeArguments = JsonSerializer.Serialize(new
        {
            command = string.Concat(Enumerable.Repeat("Write-Output keep-pair; ", 200)),
        });

        var functionCall = JsonSerializer.SerializeToElement(new
        {
            type = "function_call",
            call_id = "call-pair-1",
            name = "shell",
            arguments = hugeArguments,
            reasoning_content = "需要保留工具调用与结果的配对关系。",
        });

        var sliced = KernelTurnExecutionRuntimeHelpers.BuildSlicedResponsesFollowUpInput(
            priorInput:
            [
                KernelTurnExecutionRuntimeHelpers.CreateResponsesMessage("user", "input_text", "run tool"),
            ],
            responseItems: [functionCall],
            nextInput:
            [
                new Dictionary<string, object?>
                {
                    ["type"] = "function_call_output",
                    ["call_id"] = "call-pair-1",
                    ["output"] = "tool output",
                },
            ],
            threadId: "thread-1",
            turnId: "turn-1",
            budgetProfile: new ContextBudgetProfile { SoftBudgetTokens = 40, SafetyMarginTokens = 0 });

        var serialized = sliced.Input.Select(static item => JsonSerializer.Serialize(
            item,
            new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping })).ToArray();

        var functionCallIndex = Array.FindIndex(serialized, static item => item.Contains("\"type\":\"function_call\"", StringComparison.Ordinal));
        var toolResultIndex = Array.FindIndex(serialized, static item => item.Contains("\"type\":\"function_call_output\"", StringComparison.Ordinal));

        Assert.True(functionCallIndex >= 0, string.Join(Environment.NewLine, serialized));
        Assert.True(toolResultIndex >= 0, string.Join(Environment.NewLine, serialized));
        Assert.True(functionCallIndex < toolResultIndex, string.Join(Environment.NewLine, serialized));
        Assert.DoesNotContain(sliced.Report.DroppedSegments, static item => item.SegmentId == "responses-input-1");
    }

    [Fact]
    public void BuildSlicedResponsesFollowUpInput_WhenToolItemIdDiffers_ShouldKeepProviderCallIdPair()
    {
        const string providerCallId = "call_MDOODZHNl08XLvhyxJoZkFBQ";
        const string internalItemId = "tool_shell_call_MDOODZHNl08XLvhyxJoZkFBQ";

        var functionCall = JsonSerializer.SerializeToElement(new
        {
            type = "function_call",
            id = internalItemId,
            call_id = providerCallId,
            name = "shell",
            arguments = """{"command":"Write-Output provider-call-id"}""",
        });

        var sliced = KernelTurnExecutionRuntimeHelpers.BuildSlicedResponsesFollowUpInput(
            priorInput:
            [
                KernelTurnExecutionRuntimeHelpers.CreateResponsesMessage("user", "input_text", "run tool"),
            ],
            responseItems: [functionCall],
            nextInput:
            [
                new Dictionary<string, object?>
                {
                    ["type"] = "function_call_output",
                    ["id"] = internalItemId,
                    ["call_id"] = providerCallId,
                    ["output"] = "tool output",
                },
            ],
            threadId: "thread-1",
            turnId: "turn-1",
            budgetProfile: new ContextBudgetProfile { SoftBudgetTokens = 40, SafetyMarginTokens = 0 });

        var serialized = sliced.Input.Select(static item => JsonSerializer.Serialize(
            item,
            new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping })).ToArray();

        Assert.Contains(serialized, static item => item.Contains("\"type\":\"function_call\"", StringComparison.Ordinal)
                                                  && item.Contains("\"call_id\":\"call_MDOODZHNl08XLvhyxJoZkFBQ\"", StringComparison.Ordinal)
                                                  && item.Contains("\"id\":\"tool_shell_call_MDOODZHNl08XLvhyxJoZkFBQ\"", StringComparison.Ordinal));
        Assert.Contains(serialized, static item => item.Contains("\"type\":\"function_call_output\"", StringComparison.Ordinal)
                                                  && item.Contains("\"call_id\":\"call_MDOODZHNl08XLvhyxJoZkFBQ\"", StringComparison.Ordinal)
                                                  && item.Contains("\"id\":\"tool_shell_call_MDOODZHNl08XLvhyxJoZkFBQ\"", StringComparison.Ordinal));
        Assert.DoesNotContain(serialized, static item => item.Contains("\"call_id\":\"tool_shell_call_MDOODZHNl08XLvhyxJoZkFBQ\"", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildSlicedResponsesConversationInput_WhenInterruptedTurnHasUnmatchedFunctionCall_ShouldNotReplayCall()
    {
        var thread = new KernelThreadRecord
        {
            Id = "thread-interrupted-tool-replay",
            Turns =
            {
                new KernelTurnRecord
                {
                    Id = "turn-interrupted-tool-replay",
                    Status = "interrupted",
                    UserMessage = "run a tool",
                    Items =
                    {
                        new KernelTurnItemRecord
                        {
                            Id = "call-orphan-1",
                            Type = "function_call",
                            Payload = JsonSerializer.SerializeToElement(new
                            {
                                type = "function_call",
                                call_id = "call_orphan_1",
                                name = "shell_command",
                                arguments = """{"command":"Get-ChildItem"}""",
                            }),
                        },
                    },
                },
            },
        };

        var sliced = KernelTurnExecutionRuntimeHelpers.BuildSlicedResponsesConversationInput(
            thread,
            userText: "continue after interrupt",
            developerInstructions: null,
            contextualUserMessages: null,
            currentInputItems: null);

        var serialized = sliced.Input.Select(static item => JsonSerializer.Serialize(
            item,
            new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping })).ToArray();

        Assert.DoesNotContain(serialized, static item => item.Contains("\"type\":\"function_call\"", StringComparison.Ordinal)
                                                        && item.Contains("\"call_id\":\"call_orphan_1\"", StringComparison.Ordinal));
        Assert.Contains(serialized, static item => item.Contains("continue after interrupt", StringComparison.Ordinal));
    }

    [Fact]
    public void SliceProviderMessages_ShouldKeepCriticalDeveloperAndCurrentUser()
    {
        var messages = new List<Dictionary<string, object?>>
        {
            new()
            {
                ["role"] = "developer",
                ["content"] = "keep developer",
            },
            new()
            {
                ["role"] = "assistant",
                ["content"] = string.Concat(Enumerable.Repeat("old ", 1000)),
            },
            new()
            {
                ["role"] = "user",
                ["content"] = "keep current user",
            },
        };

        var sliced = ContextSlicingRuntimeHelpers.SliceProviderMessages(
            messages,
            budgetProfile: new ContextBudgetProfile { SoftBudgetTokens = 20, SafetyMarginTokens = 0 });

        Assert.Equal(2, sliced.Count);
        Assert.Equal("developer", sliced[0]["role"]);
        Assert.Equal("user", sliced[1]["role"]);
    }

}
